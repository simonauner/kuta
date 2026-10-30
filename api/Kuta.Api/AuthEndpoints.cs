using System.Net.Mail;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Npgsql;

public record RegisterRequest(string? Email, string? Password, string? DisplayName);
public record LoginRequest(string? Email, string? Password);
public record UserCredentials(Guid Id, string PasswordHash);
public record Me(Guid Id, string Email, string DisplayName);

public static class AuthEndpoints
{
    // PasswordHasher läser aldrig användarobjektet; typparametern och argumentet krävs bara av API:t.
    static readonly PasswordHasher<object> Hasher = new();

    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterRequest req, NpgsqlDataSource db, HttpContext http) =>
        {
            var email = NormalizeEmail(req.Email);
            var displayName = req.DisplayName?.Trim();
            var errors = new Dictionary<string, string[]>();
            if (email is null) errors["email"] = ["Ogiltig e-postadress."];
            if (req.Password is not { Length: >= 8 }) errors["password"] = ["Lösenordet måste vara minst 8 tecken."];
            if (displayName is not { Length: > 0 and <= 100 }) errors["displayName"] = ["Ange ett namn (högst 100 tecken)."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            await using var conn = await db.OpenConnectionAsync();
            try
            {
                var id = await conn.ExecuteScalarAsync<Guid>(
                    """
                    insert into users (email, display_name, password_hash)
                    values (@email, @displayName, @passwordHash)
                    returning id
                    """,
                    new { email, displayName, passwordHash = Hasher.HashPassword(null!, req.Password!) });
                await SignIn(http, id);
                return Results.NoContent();
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { error = "Det finns redan ett konto med den e-postadressen." });
            }
        });

        auth.MapPost("/login", async (LoginRequest req, NpgsqlDataSource db, HttpContext http) =>
        {
            var email = NormalizeEmail(req.Email);
            if (email is null || req.Password is null) return Results.Unauthorized();

            await using var conn = await db.OpenConnectionAsync();
            var user = await conn.QuerySingleOrDefaultAsync<UserCredentials>(
                "select id, password_hash from users where email = @email", new { email });
            if (user is null) return Results.Unauthorized();

            var result = Hasher.VerifyHashedPassword(null!, user.PasswordHash, req.Password);
            if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                await conn.ExecuteAsync("update users set password_hash = @hash where id = @id",
                    new { hash = Hasher.HashPassword(null!, req.Password), id = user.Id });

            await SignIn(http, user.Id);
            return Results.NoContent();
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return Results.NoContent();
        });

        api.MapGet("/me", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var me = await conn.QuerySingleOrDefaultAsync<Me>(
                "select id, email, display_name from users where id = @id", new { id = user.GetUserId() });
            return me is null ? Results.Unauthorized() : Results.Ok(me);
        }).RequireAuthorization();
    }

    static string? NormalizeEmail(string? email)
    {
        email = email?.Trim().ToLowerInvariant();
        // MailAddress godtar även "Namn <a@b>"; kräv att hela strängen är adressen.
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email ? email : null;
    }

    static Task SignIn(HttpContext http, Guid userId) =>
        http.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme)));
}
