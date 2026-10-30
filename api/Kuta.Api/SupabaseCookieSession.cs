using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;

// Portad (slimmad) från Kredovis. Supabase-sessionen ligger i den krypterade HttpOnly-cookien, så
// refresh-token når aldrig webbläsaren i läsbar form eller JavaScript.
public static class SupabaseCookieSession
{
    // Cookien glider så länge användaren är aktiv; PWA:n ska inte logga ut den som löper varje vecka.
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromDays(30);
    // Hård övre gräns från inloggningen, oavsett aktivitet. Ligger i user_sessions.expires_at.
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(180);
    // Förnya access-token strax innan den går ut.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    // Fråga Supabase om sessionen återkallats högst så här ofta mellan förnyelser.
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

        // Kontrolleras i vår databas vid varje anrop, så utloggning och den absoluta gränsen slår igenom direkt.
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
                // Supabase är tillfälligt otillgängligt: behåll en ännu giltig token och försök igen senare.
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

// Anrop som skickas med samma gamla cookie delar ett förnyelseresultat, eftersom Supabase roterar
// refresh-token och ser återanvändning som möjlig stöld.
// shortcut: delas bara inom en process; kör API:t på flera instanser, porta Kredovis Postgres-claim i SharedTokenRefresh.
public sealed class SharedTokenRefresh
{
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, Lazy<Task<AuthSession?>>> pending = new();

    public Task<AuthSession?> RefreshAsync(SupabaseAuthClient auth, string refreshToken)
    {
        Lazy<Task<AuthSession?>>? created = null;
        var refresh = pending.GetOrAdd(refreshToken, _ => created =
            // Inte knuten till ett enskilt anrops cancellation, eftersom andra anrop delar den
            new Lazy<Task<AuthSession?>>(() => auth.RefreshAsync(refreshToken, CancellationToken.None)));
        if (ReferenceEquals(refresh, created)) _ = ForgetAsync(refreshToken, refresh);
        return refresh.Value;
    }

    private async Task ForgetAsync(string key, Lazy<Task<AuthSession?>> refresh)
    {
        // Behåll ett lyckat resultat en stund för anrop som fortfarande bär den gamla cookien; släpp fel direkt.
        try { if (await refresh.Value is not null) await Task.Delay(ResultLifetime); }
        catch (Exception) { }
        pending.TryRemove(new KeyValuePair<string, Lazy<Task<AuthSession?>>>(key, refresh));
    }
}
