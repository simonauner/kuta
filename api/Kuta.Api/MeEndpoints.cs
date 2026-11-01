using System.Globalization;
using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

public record UpdateMeRequest(string? DisplayName);
// HasGarminData controls Garmin attribution on statistics and other views derived from the activities.
public record Me(Guid Id, string? Email, string? DisplayName, bool HrConsent, string DefaultActivityPrivacy, int ActivitiesWithOtherPrivacy,
    Supporter Supporter, bool HasGarminData, bool PublicProfile);
// Tier is null when there is no supporter membership or it has expired.
public record Supporter(string? Tier, DateTime? Until, string BadgePrivacy);
public record Profile(string DisplayName, bool HrConsent, string DefaultActivityPrivacy);
public record ActivityPrivacyRequest(string? Privacy, bool ApplyToAll = false);
public record BadgePrivacyRequest(string? Privacy);
public record PublicProfileRequest(bool Enabled);
public record DeleteAccountRequest(string? Password);

public static class MeEndpoints
{
    // auth.users.id, which is also users.id
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Look-alikes of the supporter badge (bronze, silver, gold) and of a verified mark.
    static readonly string[] BadgeLookalikes = ["✓", "✔", "✅", "☑", "🥇", "🥈", "🥉", "🏅", "🎖"];

    // Names are free text and not unique, so they are what a stranger uses to pass as a friend in a follow request.
    // Compatibility forms (full-width letters) are folded, whitespace collapsed, and invisible or direction-changing
    // characters and badge look-alikes rejected. Null when the name is not allowed.
    public static string? NormalizeDisplayName(string? name)
    {
        if (name is null) return null;
        try { name = name.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { return null; }
        name = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is 0 or > 100 || BadgeLookalikes.Any(name.Contains)) return null;
        // EnumerateRunes turns lone surrogates into the replacement character.
        foreach (var rune in name.EnumerateRunes())
            if (rune == Rune.ReplacementChar || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse
                or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
                return null;
        return name;
    }

    // Latin look-alikes in Cyrillic and Greek, in both cases since lowercasing changes some shapes (Н becomes н).
    static readonly Dictionary<char, char> Confusables = new()
    {
        ['А'] = 'a', ['В'] = 'b', ['Е'] = 'e', ['К'] = 'k', ['М'] = 'm', ['Н'] = 'h', ['О'] = 'o', ['Р'] = 'p', ['С'] = 'c',
        ['Т'] = 't', ['Х'] = 'x', ['У'] = 'y', ['І'] = 'i', ['Ј'] = 'j', ['Ѕ'] = 's', ['а'] = 'a', ['е'] = 'e', ['о'] = 'o',
        ['р'] = 'p', ['с'] = 'c', ['у'] = 'y', ['х'] = 'x', ['і'] = 'i', ['ј'] = 'j', ['ѕ'] = 's', ['ԁ'] = 'd', ['ӏ'] = 'l',
        ['һ'] = 'h', ['ԛ'] = 'q', ['ԝ'] = 'w', ['ү'] = 'y', ['Α'] = 'a', ['Β'] = 'b', ['Ε'] = 'e', ['Ζ'] = 'z', ['Η'] = 'h',
        ['Ι'] = 'i', ['Κ'] = 'k', ['Μ'] = 'm', ['Ν'] = 'n', ['Ο'] = 'o', ['Ρ'] = 'p', ['Τ'] = 't', ['Υ'] = 'y', ['Χ'] = 'x',
        ['α'] = 'a', ['ο'] = 'o', ['ν'] = 'v', ['ρ'] = 'p', ['ι'] = 'i', ['κ'] = 'k', ['υ'] = 'u', ['χ'] = 'x',
        ['0'] = 'o', ['1'] = 'l', ['i'] = 'l', ['I'] = 'l',
    };

    // What a name looks like, so that "Anna", "anna", "Аnna" (Cyrillic А), "Ánna" and "Anna 🏃" compare equal. Only for
    // warning about look-alikes, never for identity.
    // shortcut: a hand-picked subset of Unicode's confusables (UTS #39); use the full table if look-alikes get through.
    public static string NameSkeleton(string name)
    {
        var skeleton = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var mapped = Confusables.GetValueOrDefault(c, char.ToLowerInvariant(c));
            mapped = Confusables.GetValueOrDefault(mapped, mapped);
            if (char.IsLetterOrDigit(mapped)) skeleton.Append(mapped);
        }
        return skeleton.Replace("rn", "m").Replace("vv", "w").ToString();
    }

    public static void MapMe(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization();

        // Requires the password: an unlocked phone or an injected script must not be able to delete everything.
        // The Supabase session the check creates goes away with the account.
        me.MapDelete("", async ([FromBody] DeleteAccountRequest req, NpgsqlDataSource db, RawFileStore store, ImportFiles files,
            SupabaseAuthClient supabase, GarminClient garmin, IDataProtectionProvider provider, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new ApiError("Ange ditt lösenord.", "passwordRequired"));
            var email = http.User.FindFirstValue(ClaimTypes.Email)!;
            // Shares the login limit, so a stolen session cannot guess the password from many IP addresses.
            if (AuthEndpoints.LimitPerEmail(email) is { } limited) return limited;
            var (result, _) = await supabase.SignInAsync(email, req.Password, cancellationToken);
            if (result != SignInResult.SignedIn)
                return Results.Json(new ApiError("Fel lösenord.", "wrongPassword"), statusCode: StatusCodes.Status403Forbidden);
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var userId = http.User.GetUserId();
            // Garmin requires deregistration when the account is deleted; the connection then cascades with the account.
            await Garmin.DeregisterAsync(conn, garmin, provider, userId, cancellationToken);
            // Queued import archives are not cascade-deleted with the jobs; remove them after the account (the worker cleans up the rest).
            var imports = (await conn.QueryAsync<Guid>("select id from jobs where user_id = @userId and kind = @kind",
                new { userId, kind = JobKinds.StravaImport })).ToList();
            await DeleteAccountAsync(conn, store, userId, cancellationToken);
            foreach (var job in imports) files.Delete(job);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireRateLimiting("auth");

        // displayName is null until the web has created the profile with PUT after the first login
        me.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            return Results.Ok(await LoadAsync(conn, user));
        });

        me.MapPut("", async (UpdateMeRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (NormalizeDisplayName(req.DisplayName) is not { } displayName)
                return Results.BadRequest(new ApiError("Ange ett namn (högst 100 tecken, utan osynliga tecken och utan märken som liknar Kutas).", "invalidName"));

            await using var conn = await db.OpenConnectionAsync();
            // A change keeps the previous name (shown to others for 30 days) and is allowed at most once a week, so a
            // follower cannot take a friend's name unnoticed or keep switching. The first change after the name was
            // chosen is always allowed, for typos.
            var saved = await conn.ExecuteAsync(
                """
                insert into users (id, display_name, terms_version, terms_accepted_at)
                select @id, @displayName, raw_user_meta_data->>'terms_version', (raw_user_meta_data->>'terms_accepted_at')::timestamptz
                from auth.users where id = @id
                on conflict (id) do update set display_name = excluded.display_name,
                    previous_display_name = case when users.display_name <> excluded.display_name
                        then users.display_name else users.previous_display_name end,
                    display_name_changed_at = case when users.display_name <> excluded.display_name
                        then now() else users.display_name_changed_at end
                where users.display_name is null or users.display_name = excluded.display_name
                    or users.display_name_changed_at is null or users.display_name_changed_at < now() - interval '7 days'
                """,
                new { id = user.GetUserId(), displayName });
            if (saved == 0)
                return Results.Conflict(new ApiError("Du kan byta namn en gång i veckan.", "nameChangedRecently"));
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
                    "update activities set privacy = @Privacy where user_id = @id and privacy <> @Privacy and source <> 'garmin'",
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

        // Lets visitors without an account open the profile link (SocialEndpoints.MapPublic).
        me.MapPut("/public-profile", async (PublicProfileRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                "update users set public_profile = @Enabled where id = @id", new { id = user.GetUserId(), req.Enabled });
            return updated == 0
                ? Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"))
                : Results.Ok(await LoadAsync(conn, user));
        });

        // Explicit consent to storing heart rate (health data). The first timestamp is kept.
        me.MapPut("/hr-consent", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                "update users set hr_consent_at = coalesce(hr_consent_at, now()) where id = @id", new { id = user.GetUserId() });
            return updated == 0
                ? Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"))
                : Results.Ok(await LoadAsync(conn, user));
        });

        // Withdrawn consent: all stored heart rate is deleted, not just future data – including the raw files, which contain heart rate.
        // Files first: if that fails the heart rate remains in the database until the user tries again, instead of
        // remaining in storage with nothing pointing to it.
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
        // Same lock as the import: no new raw files may be stored between listing and cascade deletion.
        await conn.ExecuteAsync(new CommandDefinition("select id from users where id = @id for update",
            new { id }, tx, cancellationToken: ct));
        var keys = await conn.QueryAsync<string>(new CommandDefinition(
            "select raw_file_key from activities where user_id = @id and raw_file_key is not null for update",
            new { id }, tx, cancellationToken: ct));
        await store.DeleteAsync(keys, ct);
        // One transaction also deletes the profile, activities, equipment and both Supabase and Kuta sessions.
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
        // How many activities "apply to all" would change; Garmin activities stay private.
        var (otherPrivacy, hasGarminData, publicProfile) = await conn.QuerySingleAsync<(int, bool, bool)>("""
            select (select count(*)::int from activities where user_id = @id and privacy <> @privacy and source <> 'garmin'),
                exists (select 1 from activities where user_id = @id and source = 'garmin'),
                coalesce((select public_profile from users where id = @id), false)
            """, new { id = user.GetUserId(), privacy });
        var supporter = await conn.QuerySingleOrDefaultAsync<Supporter>("""
            select case when supporter_until > now() then supporter_tier end as tier, supporter_until as until,
                supporter_badge_privacy as badge_privacy
            from users where id = @id
            """, new { id = user.GetUserId() }) ?? new Supporter(null, null, "private");
        return new Me(user.GetUserId(), user.FindFirstValue(ClaimTypes.Email), profile?.DisplayName, profile?.HrConsent ?? false, privacy, otherPrivacy,
            supporter, hasGarminData, publicProfile);
    }
}
