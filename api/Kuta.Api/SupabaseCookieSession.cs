using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;

// The Supabase session lives in the encrypted HttpOnly cookie, so
// the refresh token never reaches the browser in readable form or JavaScript.
public static class SupabaseCookieSession
{
    // The cookie slides as long as the user is active; the PWA should not sign out someone who runs every week.
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromDays(30);
    // Hard upper limit from sign-in, regardless of activity. Stored in user_sessions.expires_at.
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(180);
    // Refresh the access token just before it expires.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    // Ask Supabase whether the session was revoked at most this often between refreshes.
    private static readonly TimeSpan ValidationInterval = TimeSpan.FromMinutes(5);

    private const string AccessTokenName = "supabase_access_token";
    private const string RefreshTokenName = "supabase_refresh_token";
    private const string SessionKey = ".kuta.session";
    private const string AccessExpiresKey = ".kuta.access_expires";
    private const string ValidatedKey = ".kuta.validated";

    public static async Task SignInAsync(HttpContext http, AuthSession session, CancellationToken cancellationToken)
    {
        await using var conn = await http.RequestServices.GetRequiredService<NpgsqlDataSource>()
            .OpenConnectionAsync(cancellationToken);
        var sessionId = await conn.ExecuteScalarAsync<Guid>(
            "insert into user_sessions (user_id, expires_at) values (@userId, now() + @lifetime) returning id",
            new { userId = session.UserId, lifetime = AbsoluteLifetime });

        var properties = new AuthenticationProperties { IsPersistent = true };
        properties.Items[SessionKey] = sessionId.ToString();
        Store(properties, session, DateTimeOffset.UtcNow);
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new Claim(ClaimTypes.Email, session.Email),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
    }

    public static async Task SignOutAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var result = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result.Properties is { } properties && TryReadSession(properties, out var sessionId))
        {
            await using var conn = await http.RequestServices.GetRequiredService<NpgsqlDataSource>()
                .OpenConnectionAsync(cancellationToken);
            await conn.ExecuteAsync("update user_sessions set ended_at = now() where id = @sessionId and ended_at is null",
                new { sessionId });
        }
        if (result.Properties?.GetTokenValue(AccessTokenName) is { } accessToken)
            await http.RequestServices.GetRequiredService<SupabaseAuthClient>().SignOutAsync(accessToken, cancellationToken);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var properties = context.Properties;
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;
        var now = DateTimeOffset.UtcNow;

        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
        var accessToken = properties.GetTokenValue(AccessTokenName);
        var refreshToken = properties.GetTokenValue(RefreshTokenName);
        if (!Guid.TryParse(userId, out var id) || email is null || accessToken is null || refreshToken is null ||
            !TryReadSession(properties, out var sessionId) || !TryRead(properties, AccessExpiresKey, out var accessExpires))
        {
            await RejectAsync(context);
            return;
        }

        // Checked in our database on every call, so sign-out and the absolute limit take effect immediately.
        await using (var conn = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(cancellationToken))
        {
            var active = await conn.ExecuteScalarAsync<bool>(
                """
                select exists (select 1 from user_sessions
                               where id = @sessionId and user_id = @id and ended_at is null and expires_at > now())
                """,
                new { sessionId, id });
            if (!active)
            {
                await RejectAsync(context);
                return;
            }
        }

        var auth = services.GetRequiredService<SupabaseAuthClient>();
        if (accessExpires - now <= RefreshMargin)
        {
            AuthSession? refreshed;
            try
            {
                refreshed = await services.GetRequiredService<SharedTokenRefresh>().RefreshAsync(auth, refreshToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Supabase is temporarily unavailable: keep a still valid token and try again later.
                if (accessExpires <= now) await RejectAsync(context);
                return;
            }

            if (refreshed is null || refreshed.UserId != id ||
                !string.Equals(refreshed.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                await RejectAsync(context);
                return;
            }
            Store(properties, refreshed, now);
            context.ShouldRenew = true;
            return;
        }

        if (!TryRead(properties, ValidatedKey, out var validated) || now - validated >= ValidationInterval)
        {
            var valid = await auth.ValidateAsync(accessToken, id, email, cancellationToken);
            if (valid == false)
            {
                await RejectAsync(context);
                return;
            }
            if (valid == true)
            {
                properties.Items[ValidatedKey] = Format(now);
                context.ShouldRenew = true;
            }
        }
    }

    private static void Store(AuthenticationProperties properties, AuthSession session, DateTimeOffset now)
    {
        properties.StoreTokens([
            new AuthenticationToken { Name = AccessTokenName, Value = session.AccessToken },
            new AuthenticationToken { Name = RefreshTokenName, Value = session.RefreshToken },
        ]);
        properties.Items[AccessExpiresKey] = Format(session.AccessTokenExpiresAt);
        properties.Items[ValidatedKey] = Format(now);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static bool TryReadSession(AuthenticationProperties properties, out Guid sessionId)
    {
        sessionId = default;
        return properties.Items.TryGetValue(SessionKey, out var text) && Guid.TryParse(text, out sessionId);
    }

    private static bool TryRead(AuthenticationProperties properties, string key, out DateTimeOffset value)
    {
        value = default;
        return properties.Items.TryGetValue(key, out var text) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }
}

// Calls sent with the same old cookie share one refresh result, since Supabase rotates
// the refresh token and treats reuse as possible theft.
// shortcut: refreshes are only coordinated within one process; use database coordination before the API runs on several instances.
public sealed class SharedTokenRefresh
{
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, Lazy<Task<AuthSession?>>> pending = new();

    public Task<AuthSession?> RefreshAsync(SupabaseAuthClient auth, string refreshToken)
    {
        Lazy<Task<AuthSession?>>? created = null;
        var refresh = pending.GetOrAdd(refreshToken, _ => created =
            // Not tied to a single call's cancellation, since other calls share it
            new Lazy<Task<AuthSession?>>(() => auth.RefreshAsync(refreshToken, CancellationToken.None)));
        if (ReferenceEquals(refresh, created)) _ = ForgetAsync(refreshToken, refresh);
        return refresh.Value;
    }

    private async Task ForgetAsync(string key, Lazy<Task<AuthSession?>> refresh)
    {
        // Keep a successful result for a while for calls still carrying the old cookie; drop failures immediately.
        try { if (await refresh.Value is not null) await Task.Delay(ResultLifetime); }
        catch (Exception) { }
        pending.TryRemove(new KeyValuePair<string, Lazy<Task<AuthSession?>>>(key, refresh));
    }
}
