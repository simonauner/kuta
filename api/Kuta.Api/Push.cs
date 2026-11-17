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
sealed record PushNote(string Kind, Guid? ActorId, string? Name, Guid? GroupId, string? GroupName);
public record PushPreferences(string[]? Disabled);
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
                await conn.ExecuteAsync("update users set push_notified_until = now() where id = @userId", new { userId });
            return Results.NoContent();
        });
        push.MapDelete("", async ([Microsoft.AspNetCore.Mvc.FromBody] PushUnsubscribeRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from push_subscriptions where user_id = @userId and endpoint = @Endpoint",
                new { userId = user.GetUserId(), req.Endpoint });
            return Results.NoContent();
        });

        // The types the user has turned off apply to every device.
        var preferences = api.MapGroup("/me/push-preferences").RequireAuthorization();
        preferences.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            return Results.Ok(new PushPreferences(await conn.QuerySingleAsync<string[]>(
                "select push_disabled from users where id = @userId", new { userId = user.GetUserId() })));
        });
        preferences.MapPut("", async (PushPreferences req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (req.Disabled is null || req.Disabled.Any(t => !Types.Contains(t)))
                return Results.BadRequest(new ApiError("Okänd notistyp.", "invalidPushPreferences"));
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync("update users set push_disabled = @disabled where id = @userId",
                new { disabled = req.Disabled.Distinct().ToArray(), userId = user.GetUserId() });
            return Results.Ok(req with { Disabled = req.Disabled.Distinct().ToArray() });
        });
    }

    const string ActiveTargets = """
        from push_subscriptions s join user_sessions us on us.id = s.session_id
        where s.user_id = @userId and us.ended_at is null and us.expires_at > now()
        """;

    public static readonly string[] Types = ["followRequest", "followAccepted", "groupJoinRequest", "groupJoinAccepted"];

    // What the user has to hear about after @since, up to @until. Group creators are accepted when the row is created,
    // which is not an approval.
    static string Events(string since, string until) => $"""
        select 'followRequest' as kind, u.id as actor_id, coalesce(u.display_name, '') as name, null::uuid as group_id, null as group_name
        from follows f join users u on u.id = f.follower_id
        where f.following_id = @userId and f.accepted_at is null and f.created_at > {since} and f.created_at <= {until}
            -- A blocked request stays hidden from the blocker.
            and {SocialEndpoints.NotBlocked("@userId", "f.follower_id")}
        union all
        select 'followAccepted', u.id, coalesce(u.display_name, ''), null, null
        from follows f join users u on u.id = f.following_id
        where f.follower_id = @userId and f.accepted_at > {since} and f.accepted_at <= {until}
        union all
        select 'groupJoinRequest', u.id, coalesce(u.display_name, ''), g.id, g.name
        from group_members m join groups g on g.id = m.group_id join users u on u.id = m.user_id
        where m.accepted_at is null and m.created_at > {since} and m.created_at <= {until}
            and exists (select 1 from group_members a where a.group_id = m.group_id and a.user_id = @userId and a.role = 'admin')
        union all
        select 'groupJoinAccepted', null, null, g.id, g.name
        from group_members m join groups g on g.id = m.group_id
        where m.user_id = @userId and m.accepted_at > m.created_at and m.accepted_at > {since} and m.accepted_at <= {until}
        """;

    // Queues a push job when the user has something new to hear about and a device to hear it on.
    public static async Task EnqueueIfPendingAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct = default)
    {
        if (await conn.ExecuteScalarAsync<bool>(new CommandDefinition($"""
                select exists (select 1 {ActiveTargets}) and exists (
                    select 1 from users n, lateral ({Events("n.push_notified_until", "'infinity'")}) e where n.id = @userId)
                """, new { userId }, cancellationToken: ct)))
            await JobQueue.EnqueueAsync(conn, userId, JobKinds.Push, ct: ct);
    }

    // Moves the cursor before sending: a notification may be lost, but is never repeated.
    // shortcut: an event whose transaction commits after the job read it, with a timestamp before the cursor, is missed;
    // the window is that transaction's length (milliseconds). Store events in an outbox if that ever matters.
    public static async Task<PushJobResult> RunJobAsync(NpgsqlConnection conn, WebPushSender sender, JobRow job,
        CancellationToken ct)
    {
        var userId = job.UserId;
        List<PushNote> notes;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            notes = (await conn.QueryAsync<PushNote>(new CommandDefinition($"""
                select e.* from users n, lateral ({Events("n.push_notified_until", "now()")}) e
                where n.id = @userId and e.kind <> all (n.push_disabled)
                """, new { userId }, tx, cancellationToken: ct))).ToList();
            await conn.ExecuteAsync(new CommandDefinition(
                "update users set push_notified_until = now() where id = @userId", new { userId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
        }
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
    static object Message(PushNote n, string locale)
    {
        var sv = locale == "sv";
        return n.Kind switch
        {
            "followRequest" => new { title = sv ? "Ny följförfrågan" : "New follow request",
                body = sv ? $"{n.Name} vill följa dig." : $"{n.Name} wants to follow you.", url = "/app", tag = "follow-request" },
            "followAccepted" => new { title = sv ? "Förfrågan godkänd" : "Request accepted",
                body = sv ? $"{n.Name} godkände din följförfrågan." : $"{n.Name} accepted your follow request.",
                url = $"/app/runners/{n.ActorId}", tag = $"follow-accepted-{n.ActorId}" },
            "groupJoinRequest" => new { title = n.GroupName,
                body = sv ? $"{n.Name} vill gå med i gruppen." : $"{n.Name} wants to join the group.",
                url = $"/app/groups/{n.GroupId}", tag = $"group-request-{n.GroupId}" },
            _ => new { title = n.GroupName,
                body = sv ? "Du är nu med i gruppen." : "You are now a member of the group.",
                url = $"/app/groups/{n.GroupId}", tag = $"group-accepted-{n.GroupId}" },
        };
    }
}
