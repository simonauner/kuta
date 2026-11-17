using System.Buffers.Binary;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;

public record PushSubscriptionRequest(string? Endpoint, string? P256dh, string? Auth, string? Locale);
public record PushUnsubscribeRequest(string? Endpoint);
sealed record PushTarget(Guid Id, string Endpoint, string P256dh, string Auth, string Locale);
sealed record PushNote(string Kind, Guid ActorId, string Name, bool Skip);
public sealed record PushJobResult(int Notifications, int Sent);

// Payload encryption (RFC 8291, aes128gcm from RFC 8188) and the VAPID signature (RFC 8292).
public static class WebPushCrypto
{
    public static byte[] Encrypt(byte[] payload, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman serverKey,
        byte[] salt)
    {
        using var userAgent = ECDiffieHellman.Create(PublicKey(userAgentPublicKey));
        var serverPublicKey = PublicKeyBytes(serverKey.ExportParameters(false));
        var sharedSecret = serverKey.DeriveRawSecretAgreement(userAgent.PublicKey);
        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), userAgentPublicKey, serverPublicKey);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret), 32, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var key = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        // A single record: the payload followed by the last-record delimiter, without padding.
        var plaintext = Concat(payload, [2]);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length)) aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var recordSize = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(recordSize, 4096);
        return Concat(salt, recordSize, [(byte)serverPublicKey.Length], serverPublicKey, ciphertext, tag);
    }

    public static string VapidToken(string audience, string subject, DateTimeOffset expires, ECDsa key)
    {
        var header = Base64Url("""{"typ":"JWT","alg":"ES256"}"""u8.ToArray());
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object> { ["aud"] = audience, ["exp"] = expires.ToUnixTimeSeconds(), ["sub"] = subject }));
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    public static ECParameters PublicKey(byte[] point) => point is [4, ..] && point.Length == 65
        ? new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..] } }
        : throw new CryptographicException();

    public static byte[] PublicKeyBytes(ECParameters parameters) => Concat([4], parameters.Q.X!, parameters.Q.Y!);

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));

    static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}

public enum PushResult { Sent, Gone, Failed }

// Sends to the browser's push service. Without Push:PrivateKey (a base64 SEC1 DER P-256 key) push is off.
public sealed class WebPushSender(HttpClient http, IConfiguration configuration)
{
    readonly ECDsa? vapidKey = LoadKey(configuration["Push:PrivateKey"]);
    readonly string subject = configuration["Push:Subject"] ?? "https://kutaapp.com";

    public string? PublicKey => vapidKey is null ? null : WebPushCrypto.Base64Url(WebPushCrypto.PublicKeyBytes(vapidKey.ExportParameters(false)));

    static ECDsa? LoadKey(string? der)
    {
        if (string.IsNullOrWhiteSpace(der)) return null;
        var key = ECDsa.Create();
        key.ImportECPrivateKey(Convert.FromBase64String(der), out _);
        return key;
    }

    internal async Task<PushResult> SendAsync(PushTarget target, object message, CancellationToken ct)
    {
        if (vapidKey is null) return PushResult.Failed;
        using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var body = WebPushCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(message, Json),
            WebPushCrypto.FromBase64Url(target.P256dh), WebPushCrypto.FromBase64Url(target.Auth), serverKey,
            RandomNumberGenerator.GetBytes(16));
        var endpoint = new Uri(target.Endpoint);
        var token = WebPushCrypto.VapidToken(endpoint.GetLeftPart(UriPartial.Authority), subject,
            DateTimeOffset.UtcNow.AddHours(12), vapidKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={token}, k={PublicKey}");
        request.Headers.Add("TTL", "86400");
        try
        {
            using var response = await http.SendAsync(request, ct);
            return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone ? PushResult.Gone
                : response.IsSuccessStatusCode ? PushResult.Sent : PushResult.Failed;
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return PushResult.Failed;
        }
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

// Push notifications for follow requests and approvals. Each browser subscribes separately and is tied to its sign-in
// session, so a device stops getting notifications when it signs out or the session ends.
public static class Push
{
    public const int MaxSubscriptions = 10;
    // The server posts to the endpoint the browser gives, so only the browsers' push services are allowed (no SSRF).
    static readonly string[] PushServices =
        ["fcm.googleapis.com", ".push.services.mozilla.com", ".notify.windows.com", ".push.apple.com"];

    public static bool IsPushService(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && endpoint.Length <= 2048 && PushServices.Any(s => s.StartsWith('.') ? uri.Host.EndsWith(s) : uri.Host == s);

    static bool IsKey(string? value, int length)
    {
        try { return value is not null && WebPushCrypto.FromBase64Url(value).Length == length; }
        catch (FormatException) { return false; }
    }

    public static void MapPush(this RouteGroupBuilder api)
    {
        api.MapGet("/push/key", (WebPushSender sender) => Results.Ok(new { publicKey = sender.PublicKey }));

        var push = api.MapGroup("/me/push-subscriptions").RequireAuthorization();
        push.MapPut("", async (PushSubscriptionRequest req, HttpContext http, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (!IsPushService(req.Endpoint) || !IsKey(req.P256dh, 65) || !IsKey(req.Auth, 16) || req.Locale is not ("sv" or "en"))
                return Results.BadRequest(new ApiError("Notiser kan inte slås på i den här webbläsaren.", "invalidPushSubscription"));
            if (await SupabaseCookieSession.SessionIdAsync(http) is not { } sessionId) return Results.Unauthorized();
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            // The web sends the subscription again on later visits, which moves it to the current session (and account).
            var first = !await conn.ExecuteScalarAsync<bool>(
                "select exists(select 1 from push_subscriptions where user_id = @userId)", new { userId });
            await conn.ExecuteAsync("""
                insert into push_subscriptions (user_id, session_id, endpoint, p256dh, auth, locale)
                values (@userId, @sessionId, @Endpoint, @P256dh, @Auth, @Locale)
                on conflict (endpoint) do update set user_id = excluded.user_id, session_id = excluded.session_id,
                    p256dh = excluded.p256dh, auth = excluded.auth, locale = excluded.locale, created_at = now();
                delete from push_subscriptions where user_id = @userId and id not in (
                    select id from push_subscriptions where user_id = @userId order by created_at desc limit @max);
                """, new { userId, sessionId, req.Endpoint, req.P256dh, req.Auth, req.Locale, max = MaxSubscriptions });
            // The first device should not get a burst of old requests and approvals at the next event.
            if (first)
                await conn.ExecuteAsync("""
                    update follows set request_notified = true where following_id = @userId and not request_notified;
                    update follows set accept_notified = true where follower_id = @userId and not accept_notified;
                    """, new { userId });
            return Results.NoContent();
        });
        push.MapDelete("", async ([Microsoft.AspNetCore.Mvc.FromBody] PushUnsubscribeRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from push_subscriptions where user_id = @userId and endpoint = @Endpoint",
                new { userId = user.GetUserId(), req.Endpoint });
            return Results.NoContent();
        });
    }

    const string ActiveTargets = """
        from push_subscriptions s join user_sessions us on us.id = s.session_id
        where s.user_id = @userId and us.ended_at is null and us.expires_at > now()
        """;

    // Queues a push job when the user has something new to hear about and a device to hear it on.
    public static async Task EnqueueIfPendingAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct = default)
    {
        if (await conn.ExecuteScalarAsync<bool>(new CommandDefinition($"""
                select exists (select 1 {ActiveTargets})
                    and (exists (select 1 from follows where following_id = @userId and not request_notified)
                        or exists (select 1 from follows where follower_id = @userId and not accept_notified))
                """, new { userId }, cancellationToken: ct)))
            await JobQueue.EnqueueAsync(conn, userId, JobKinds.Push, ct: ct);
    }

    // Marks before sending: a notification may be lost, but is never repeated.
    public static async Task<PushJobResult> RunJobAsync(NpgsqlConnection conn, WebPushSender sender, JobRow job,
        CancellationToken ct)
    {
        var userId = job.UserId;
        var notes = (await conn.QueryAsync<PushNote>(new CommandDefinition($"""
            with requests as (
                update follows f set request_notified = true where f.following_id = @userId and not f.request_notified
                returning f.follower_id as actor_id, f.accepted_at is not null as accepted),
            accepts as (
                update follows f set accept_notified = true where f.follower_id = @userId and not f.accept_notified
                returning f.following_id as actor_id)
            select 'followRequest' as kind, u.id as actor_id, coalesce(u.display_name, '') as name,
                -- Already handled, or a blocked request, which stays hidden from the blocker.
                (r.accepted or not {SocialEndpoints.NotBlocked("@userId", "u.id")}) as skip
            from requests r join users u on u.id = r.actor_id
            union all
            select 'followAccepted', u.id, coalesce(u.display_name, ''), false from accepts a join users u on u.id = a.actor_id
            """, new { userId }, cancellationToken: ct))).Where(n => !n.Skip).ToList();
        if (notes.Count == 0) return new(0, 0);

        var targets = await conn.QueryAsync<PushTarget>(new CommandDefinition(
            $"select s.id, s.endpoint, s.p256dh, s.auth, s.locale {ActiveTargets}", new { userId }, cancellationToken: ct));
        var sent = 0;
        foreach (var target in targets)
            foreach (var note in notes)
            {
                var result = await sender.SendAsync(target, Message(note, target.Locale), ct);
                if (result == PushResult.Sent) sent++;
                if (result != PushResult.Gone) continue;
                await conn.ExecuteAsync(new CommandDefinition("delete from push_subscriptions where id = @Id", target,
                    cancellationToken: ct));
                break;
            }
        return new(notes.Count, sent);
    }

    // The notification texts are rendered here, like the email templates, since the service worker has no translations.
    static object Message(PushNote note, string locale) => (note.Kind, locale) switch
    {
        ("followRequest", "sv") => new { title = "Ny följförfrågan", body = $"{note.Name} vill följa dig.", url = "/app", tag = "follow-request" },
        ("followRequest", _) => new { title = "New follow request", body = $"{note.Name} wants to follow you.", url = "/app", tag = "follow-request" },
        (_, "sv") => new { title = "Förfrågan godkänd", body = $"{note.Name} godkände din följförfrågan.", url = $"/app/runners/{note.ActorId}", tag = $"follow-accepted-{note.ActorId}" },
        _ => new { title = "Request accepted", body = $"{note.Name} accepted your follow request.", url = $"/app/runners/{note.ActorId}", tag = $"follow-accepted-{note.ActorId}" },
    };
}
