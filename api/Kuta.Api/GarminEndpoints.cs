using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

public record GarminStatus(bool Available, bool Connected, bool ActivityExport, DateTime? ConnectedAt);
public record GarminConnectStart(string Url);

public static class GarminEndpoints
{
    sealed record OAuthTicket(Guid UserId, string State, string Verifier);
    sealed record Connection(bool ActivityExport, DateTime CreatedAt);

    static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(15);

    // Garmin's push service sends from these networks (Activity API, section 4). Push has no signature and a
    // deregistration deletes data, so other senders are rejected.
    static readonly IPNetwork[] PushNetworks = [.. new[]
    {
        "204.77.162.0/23", "204.77.163.0/24", "98.199.124.0/23", "185.132.79.0/24", "208.65.206.0/23",
        "185.207.94.0/23", "198.233.176.0/24",
    }.Select(IPNetwork.Parse)];

    static bool IsDevelopment(HttpContext http) => http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
    static string CookieName(HttpContext http) => IsDevelopment(http) ? "kuta.garmin-oauth" : "__Secure-kuta.garmin-oauth";

    static ITimeLimitedDataProtector Protector(HttpContext http) =>
        http.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("kuta.garmin-oauth.v1").ToTimeLimitedDataProtector();

    // Lax: the cookie must be sent when Garmin redirects the browser back.
    static CookieOptions Cookie(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = !IsDevelopment(http) || http.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/api/garmin/callback",
    };

    static string RedirectUri(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}/api/garmin/callback";
    static IResult BackToSettings(string result) => Results.Redirect($"/app/settings/import?garmin={result}");

    public static void MapGarmin(this RouteGroupBuilder api)
    {
        var garmin = api.MapGroup("/garmin").RequireAuthorization();

        garmin.MapGet("", async (NpgsqlDataSource db, GarminClient client, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var connection = await conn.QuerySingleOrDefaultAsync<Connection>(new CommandDefinition(
                "select @permission = any(permissions) as activity_export, created_at from garmin_connections where user_id = @userId",
                new { userId = user.GetUserId(), permission = GarminClient.ActivityExport }, cancellationToken: ct));
            return Results.Ok(new GarminStatus(client.AvailableTo(user.GetUserId()), connection is not null,
                connection?.ActivityExport ?? false, connection?.CreatedAt));
        });

        // Puts a PKCE verifier and state in an encrypted cookie and returns the URL of Garmin's consent page.
        garmin.MapPost("/connect", (HttpContext http, GarminClient client, ClaimsPrincipal user) =>
        {
            if (!client.AvailableTo(user.GetUserId()))
                return Results.Json(new ApiError("Garmin Connect är inte tillgängligt just nu.", "garminUnavailable"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            var ticket = new OAuthTicket(user.GetUserId(), GarminClient.Base64Url(RandomNumberGenerator.GetBytes(32)),
                GarminClient.Base64Url(RandomNumberGenerator.GetBytes(64)));
            var expires = DateTimeOffset.UtcNow.Add(TicketLifetime);
            var options = Cookie(http);
            options.Expires = expires;
            http.Response.Cookies.Append(CookieName(http),
                Protector(http).Protect(JsonSerializer.Serialize(ticket), expires), options);
            var query = QueryString.Create(new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = client.ClientId,
                ["code_challenge"] = GarminClient.Challenge(ticket.Verifier),
                ["code_challenge_method"] = "S256",
                ["redirect_uri"] = RedirectUri(http),
                ["state"] = ticket.State,
            });
            return Results.Ok(new GarminConnectStart(GarminClient.AuthorizeUrl + query));
        });

        // Garmin redirects the browser here (GET per OAuth). The connection is saved only when the state matches the
        // cookie for the same signed-in user.
        garmin.MapGet("/callback", async (string? code, string? state, HttpContext http, GarminClient client,
            NpgsqlDataSource db, IDataProtectionProvider provider, ClaimsPrincipal user, CancellationToken ct) =>
        {
            OAuthTicket? ticket = null;
            if (http.Request.Cookies.TryGetValue(CookieName(http), out var value))
            {
                try { ticket = JsonSerializer.Deserialize<OAuthTicket>(Protector(http).Unprotect(value)); }
                catch (CryptographicException) { }
                catch (JsonException) { }
            }
            http.Response.Cookies.Delete(CookieName(http), Cookie(http));
            if (ticket is null || ticket.UserId != user.GetUserId() || state != ticket.State)
                return BackToSettings("failed");
            if (string.IsNullOrEmpty(code)) return BackToSettings("declined");
            try
            {
                if (await client.ExchangeCodeAsync(code, ticket.Verifier, RedirectUri(http), ct) is not { } tokens)
                    return BackToSettings("failed");
                var garminUserId = await client.GetUserIdAsync(tokens.AccessToken, ct);
                var permissions = await client.GetPermissionsAsync(tokens.AccessToken, ct);
                await using var conn = await db.OpenConnectionAsync(ct);
                if (!await Garmin.SaveConnectionAsync(conn, provider, ticket.UserId, garminUserId, tokens, permissions, ct))
                    return BackToSettings("inUse");
            }
            catch (Exception e) when (e is GarminUnavailableException or HttpRequestException)
            {
                return BackToSettings("unavailable");
            }
            return BackToSettings("connected");
        });

        // Deregisters at Garmin and deletes everything retrieved from it.
        garmin.MapDelete("", async (NpgsqlDataSource db, RawFileStore store, GarminClient client,
            IDataProtectionProvider provider, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var userId = user.GetUserId();
            await Garmin.DeregisterAsync(conn, client, provider, userId, ct);
            await Garmin.DeleteDataAsync(conn, store, userId, ct);
            return Results.NoContent();
        });
    }

    // Outside the /api group: Garmin has neither a session nor a CSRF token. Responds 200 as soon as the notices are stored.
    public static void MapGarminPush(this WebApplication app)
    {
        app.MapPost("/api/garmin/push", async (HttpRequest request, NpgsqlDataSource db, RawFileStore store,
            IConfiguration configuration, CancellationToken ct) =>
        {
            // Checked before the body is read, so senders outside Garmin cannot make the API parse large bodies.
            if (configuration.GetValue("Garmin:VerifyPushSource", true) && !FromGarmin(request.HttpContext.Connection.RemoteIpAddress))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            GarminPush? push;
            try { push = await request.ReadFromJsonAsync<GarminPush>(ct); }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { push = null; }
            if (push is null) return Results.BadRequest();
            await using var conn = await db.OpenConnectionAsync(ct);
            await Garmin.HandlePushAsync(conn, store, push, ct);
            return Results.Ok();
        });
    }

    public static bool FromGarmin(IPAddress? address) =>
        address is not null && PushNetworks.Any(n => n.Contains(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address));
}
