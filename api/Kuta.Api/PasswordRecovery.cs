using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

// En separat cookie ger bara rätt att byta lösenord, aldrig tillgång till appens data.
public static class PasswordRecovery
{
    private record Ticket(Guid Id, Guid UserId, string AccessToken);
    private static string CookieName(HttpContext http) =>
        http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() ? "kuta.recovery" : "__Secure-kuta.recovery";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static ITimeLimitedDataProtector Protector(HttpContext http) =>
        http.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("kuta.password-recovery.v1").ToTimeLimitedDataProtector();

    private static CookieOptions Cookie(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = !http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() || http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth/reset-password",
    };

    public static async Task StartAsync(HttpContext http, AuthSession session, CancellationToken ct)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        if (session.AccessTokenExpiresAt < expires) expires = session.AccessTokenExpiresAt;
        await using var conn = await http.RequestServices.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<Guid>(
            "insert into user_sessions (user_id, expires_at) values (@userId, @expires) returning id",
            new { userId = session.UserId, expires });
        var options = Cookie(http);
        options.Expires = expires;
        http.Response.Cookies.Append(CookieName(http),
            Protector(http).Protect(JsonSerializer.Serialize(new Ticket(id, session.UserId, session.AccessToken)), expires), options);
    }

    public static async Task<IResult> ResetAsync(string? password, HttpContext http, SupabaseAuthClient supabase,
        NpgsqlDataSource db, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return Results.BadRequest(new ApiError("Använd minst 8 tecken i lösenordet."));

        Ticket? ticket = null;
        if (http.Request.Cookies.TryGetValue(CookieName(http), out var value))
        {
            try { ticket = JsonSerializer.Deserialize<Ticket>(Protector(http).Unprotect(value)); }
            catch (CryptographicException) { }
            catch (JsonException) { }
        }
        if (ticket is null) return InvalidLink(http);

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var transaction = await conn.BeginTransactionAsync(ct);
        // Samordna även olika återställningslänkar för samma konto.
        await conn.ExecuteAsync("select pg_advisory_xact_lock(hashtextextended(@userId, 0))",
            new { userId = ticket.UserId.ToString() }, transaction);
        // Låset gör även en kopia av cookien oanvändbar efter ett lyckat byte.
        var active = await conn.QuerySingleOrDefaultAsync<Guid?>(
            """
            select id from user_sessions
            where id = @id and user_id = @userId and ended_at is null and expires_at > now()
            for update
            """, new { ticket.Id, ticket.UserId }, transaction);
        if (active is null) return InvalidLink(http);

        var result = await supabase.ResetPasswordAsync(ticket.AccessToken, password, ct);
        if (result == PasswordResetResult.WeakPassword)
            return Results.BadRequest(new ApiError("Lösenordet är för svagt. Använd minst 8 tecken."));
        if (result == PasswordResetResult.SamePassword)
            return Results.BadRequest(new ApiError("Välj ett annat lösenord än ditt nuvarande."));
        if (result != PasswordResetResult.Updated) return InvalidLink(http);

        await conn.ExecuteAsync("update user_sessions set ended_at = now() where user_id = @userId and ended_at is null",
            new { ticket.UserId }, transaction);
        await transaction.CommitAsync(ct);
        http.Response.Cookies.Delete(CookieName(http), Cookie(http));
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await supabase.SignOutAsync(ticket.AccessToken, ct);
        return Results.NoContent();
    }

    private static IResult InvalidLink(HttpContext http)
    {
        http.Response.Cookies.Delete(CookieName(http), Cookie(http));
        return Results.Json(new ApiError("Återställningslänken är ogiltig eller har gått ut. Begär en ny länk."), statusCode: 401);
    }
}
