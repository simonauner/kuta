using System.Security.Claims;
using Dapper;
using Npgsql;

public record UpdateMeRequest(string? DisplayName);
public record Me(Guid Id, string? Email, string? DisplayName, bool HrConsent);
public record Profile(string DisplayName, bool HrConsent);

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
            return Results.Ok(await LoadAsync(conn, user));
        });

        me.MapPut("", async (UpdateMeRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            var displayName = req.DisplayName?.Trim();
            if (displayName is not { Length: > 0 and <= 100 })
                return Results.BadRequest(new ApiError("Ange ett namn (högst 100 tecken).", "invalidName"));

            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                insert into users (id, display_name) values (@id, @displayName)
                on conflict (id) do update set display_name = excluded.display_name
                """,
                new { id = user.GetUserId(), displayName });
            return Results.Ok(await LoadAsync(conn, user));
        });

        // Uttryckligt samtycke till att puls sparas (hälsodata). Första tidpunkten behålls.
        me.MapPut("/hr-consent", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                "update users set hr_consent_at = coalesce(hr_consent_at, now()) where id = @id", new { id = user.GetUserId() });
            return updated == 0
                ? Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"))
                : Results.Ok(await LoadAsync(conn, user));
        });

        // Återkallat samtycke: all sparad puls raderas, inte bara framtida – även råfilerna, som innehåller puls.
        // Filerna först: misslyckas det finns pulsen kvar i databasen tills användaren försöker igen, i stället för
        // att ligga kvar i lagringen utan att något pekar på den.
        me.MapDelete("/hr-consent", async (NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var id = user.GetUserId();
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            await conn.ExecuteAsync("select id from users where id = @id for update", new { id }, tx);
            var keys = await conn.QueryAsync<string>(
                "select raw_file_key from activities where user_id = @id and raw_file_key is not null", new { id }, tx);
            await store.DeleteAsync(keys, cancellationToken);

            await conn.ExecuteAsync("update users set hr_consent_at = null where id = @id", new { id }, tx);
            await conn.ExecuteAsync(
                "update activities set avg_hr = null, max_hr = null, raw_file_key = null where user_id = @id", new { id }, tx);
            await conn.ExecuteAsync(
                "update activity_streams s set hr = null from activities a where a.id = s.activity_id and a.user_id = @id",
                new { id }, tx);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(await LoadAsync(conn, user));
        });
    }

    public static async Task<Profile?> LoadProfileAsync(NpgsqlConnection conn, Guid id) =>
        await conn.QuerySingleOrDefaultAsync<Profile>(
            "select display_name, hr_consent_at is not null as hr_consent from users where id = @id", new { id });

    private static async Task<Me> LoadAsync(NpgsqlConnection conn, ClaimsPrincipal user)
    {
        var profile = await LoadProfileAsync(conn, user.GetUserId());
        return new Me(user.GetUserId(), user.FindFirstValue(ClaimTypes.Email), profile?.DisplayName, profile?.HrConsent ?? false);
    }
}
