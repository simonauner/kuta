using System.Security.Claims;
using Dapper;
using Npgsql;

public record UpdateMeRequest(string? DisplayName);
public record Me(Guid Id, string? Email, string? DisplayName);

public static class MeEndpoints
{
    // auth.users.id, som också är users.id
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapMe(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization();

        // displayName är null tills webben skapat profilen med PUT efter första inloggningen
        me.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var displayName = await conn.ExecuteScalarAsync<string?>(
                "select display_name from users where id = @id", new { id = user.GetUserId() });
            return Results.Ok(new Me(user.GetUserId(), user.FindFirstValue(ClaimTypes.Email), displayName));
        });

        me.MapPut("", async (UpdateMeRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            var displayName = req.DisplayName?.Trim();
            if (displayName is not { Length: > 0 and <= 100 })
                return Results.BadRequest(new ApiError("Ange ett namn (högst 100 tecken)."));

            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                insert into users (id, display_name) values (@id, @displayName)
                on conflict (id) do update set display_name = excluded.display_name
                """,
                new { id = user.GetUserId(), displayName });
            return Results.Ok(new Me(user.GetUserId(), user.FindFirstValue(ClaimTypes.Email), displayName));
        });
    }
}
