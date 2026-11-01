using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

public record UpdateMeRequest(string? DisplayName);
// HasGarminData styr Garmins märkning av statistik och andra vyer härledda ur passen.
public record Me(Guid Id, string? Email, string? DisplayName, bool HrConsent, string DefaultActivityPrivacy, bool AllActivitiesMatchDefaultPrivacy,
    Supporter Supporter, bool HasGarminData);
// Tier är null när supporterskapet saknas eller har gått ut.
public record Supporter(string? Tier, DateTime? Until, string BadgePrivacy);
public record Profile(string DisplayName, bool HrConsent, string DefaultActivityPrivacy);
public record ActivityPrivacyRequest(string? Privacy, bool ApplyToAll = false);
public record BadgePrivacyRequest(string? Privacy);
public record DeleteAccountRequest(string? Password);

public static class MeEndpoints
{
    // auth.users.id, som också är users.id
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapMe(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization();

        // Kräver lösenordet: en upplåst telefon eller ett injicerat skript ska inte kunna radera allt.
        // Supabase-sessionen som kontrollen skapar försvinner med kontot.
        me.MapDelete("", async ([FromBody] DeleteAccountRequest req, NpgsqlDataSource db, RawFileStore store, ImportFiles files,
            SupabaseAuthClient supabase, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new ApiError("Ange ditt lösenord.", "passwordRequired"));
            var (result, _) = await supabase.SignInAsync(http.User.FindFirstValue(ClaimTypes.Email)!, req.Password, cancellationToken);
            if (result != SignInResult.SignedIn)
                return Results.Json(new ApiError("Fel lösenord.", "wrongPassword"), statusCode: StatusCodes.Status403Forbidden);
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var userId = http.User.GetUserId();
            // Köade importarkiv kaskadraderas inte med jobben; ta bort dem efter kontot (arbetaren städar resten).
            var imports = (await conn.QueryAsync<Guid>("select id from jobs where user_id = @userId and kind = @kind",
                new { userId, kind = JobKinds.StravaImport })).ToList();
            await DeleteAccountAsync(conn, store, userId, cancellationToken);
            foreach (var job in imports) files.Delete(job);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireRateLimiting("auth");

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
                insert into users (id, display_name, terms_version, terms_accepted_at)
                select @id, @displayName, raw_user_meta_data->>'terms_version', (raw_user_meta_data->>'terms_accepted_at')::timestamptz
                from auth.users where id = @id
                on conflict (id) do update set display_name = excluded.display_name
                """,
                new { id = user.GetUserId(), displayName });
            return Results.Ok(await LoadAsync(conn, user));
        });

        me.MapPut("/activity-privacy", async (ActivityPrivacyRequest req, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!ActivityInput.IsValidPrivacy(req.Privacy))
                return Results.BadRequest(new ApiError("Välj giltig synlighet.", "invalidPrivacy"));

            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var id = user.GetUserId();
            // The profile update takes the same lock as imports, so bulk changes include preceding imports.
            var updated = await conn.ExecuteAsync(new CommandDefinition(
                "update users set default_activity_privacy = @Privacy where id = @id",
                new { id, req.Privacy }, tx, cancellationToken: ct));
            if (updated == 0)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            if (req.ApplyToAll)
                await conn.ExecuteAsync(new CommandDefinition(
                    "update activities set privacy = @Privacy where user_id = @id and privacy <> @Privacy",
                    new { id, req.Privacy }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return Results.Ok(await LoadAsync(conn, user));
        });

        me.MapPut("/supporter-badge-privacy", async (BadgePrivacyRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (!ActivityInput.IsValidPrivacy(req.Privacy))
                return Results.BadRequest(new ApiError("Välj giltig synlighet.", "invalidPrivacy"));
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                "update users set supporter_badge_privacy = @Privacy where id = @id", new { id = user.GetUserId(), req.Privacy });
            return updated == 0
                ? Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"))
                : Results.Ok(await LoadAsync(conn, user));
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
            await conn.ExecuteAsync(
                "update activity_laps l set avg_hr = null, max_hr = null from activities a where a.id = l.activity_id and a.user_id = @id",
                new { id }, tx);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(await LoadAsync(conn, user));
        });
    }

    public static async Task DeleteAccountAsync(NpgsqlConnection conn, RawFileStore store, Guid id,
        CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Samma lås som importen: inga nya råfiler får sparas mellan listning och kaskadradering.
        await conn.ExecuteAsync(new CommandDefinition("select id from users where id = @id for update",
            new { id }, tx, cancellationToken: ct));
        var keys = await conn.QueryAsync<string>(new CommandDefinition(
            "select raw_file_key from activities where user_id = @id and raw_file_key is not null for update",
            new { id }, tx, cancellationToken: ct));
        await store.DeleteAsync(keys, ct);
        // En transaktion raderar även profil, pass, utrustning och både Supabase- och Kuta-sessioner.
        await conn.ExecuteAsync(new CommandDefinition("delete from auth.users where id = @id",
            new { id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public static async Task<Profile?> LoadProfileAsync(NpgsqlConnection conn, Guid id) =>
        await conn.QuerySingleOrDefaultAsync<Profile>(
            "select display_name, hr_consent_at is not null as hr_consent, default_activity_privacy from users where id = @id", new { id });

    private static async Task<Me> LoadAsync(NpgsqlConnection conn, ClaimsPrincipal user)
    {
        var profile = await LoadProfileAsync(conn, user.GetUserId());
        var privacy = profile?.DefaultActivityPrivacy ?? "private";
        var (allMatch, hasGarminData) = await conn.QuerySingleAsync<(bool, bool)>("""
            select not exists (select 1 from activities where user_id = @id and privacy <> @privacy),
                exists (select 1 from activities where user_id = @id and source = 'garmin')
            """, new { id = user.GetUserId(), privacy });
        var supporter = await conn.QuerySingleOrDefaultAsync<Supporter>("""
            select case when supporter_until > now() then supporter_tier end as tier, supporter_until as until,
                supporter_badge_privacy as badge_privacy
            from users where id = @id
            """, new { id = user.GetUserId() }) ?? new Supporter(null, null, "private");
        return new Me(user.GetUserId(), user.FindFirstValue(ClaimTypes.Email), profile?.DisplayName, profile?.HrConsent ?? false, privacy, allMatch,
            supporter, hasGarminData);
    }
}
