using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Npgsql;

public static class DataExport
{
    public static void MapDataExport(this RouteGroupBuilder api)
    {
        api.MapGet("/me/export", async (NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user,
            HttpResponse response, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var archive = await CreateAsync(conn, store, user.GetUserId(), ct);
            response.Headers.CacheControl = "no-store";
            return Results.File(archive, "application/zip", "kuta-export.zip");
        }).RequireAuthorization();
    }

    public static async Task<FileStream> CreateAsync(NpgsqlConnection conn, RawFileStore store, Guid userId,
        CancellationToken ct)
    {
        // Bygg klart före svaret: lagringsfel får aldrig ge en lyckad men ofullständig export.
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var file = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options);
        try
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            // Samma låsordning som samtyckesåterkallelse; skydda råfilerna även mot samtidig passradering.
            var profile = await conn.QuerySingleOrDefaultAsync<ExportProfile>(new CommandDefinition("""
                select u.id, a.email, u.display_name, u.created_at, u.hr_consent_at, u.default_activity_privacy
                from users u join auth.users a on a.id = u.id where u.id = @userId for share of u
                """, new { userId }, tx, cancellationToken: ct));
            var activities = await conn.QueryAsync<ExportActivity>(new CommandDefinition("""
                select id, source, created_at, privacy, strava_activity_id, raw_file_key
                from activities where user_id = @userId order by id for share
                """, new { userId }, tx, cancellationToken: ct));
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteJsonAsync(zip, "profile.json", profile, ct);
                await WriteJsonAsync(zip, "equipment.json", await EquipmentEndpoints.LoadAsync(conn, userId), ct);
                await WriteJsonAsync(zip, "follows.json", await conn.QueryAsync<ExportFollow>(new CommandDefinition("""
                    select follower_id, following_id, accepted_at, created_at from follows
                    where follower_id = @userId or following_id = @userId order by follower_id, following_id
                    """, new { userId }, tx, cancellationToken: ct)), ct);
                await WriteJsonAsync(zip, "privacy-places.json", await PrivacyPlaces.LoadAsync(conn, userId, ct, tx), ct);
                foreach (var activity in activities)
                {
                    ct.ThrowIfCancellationRequested();
                    var detail = await ActivityEndpoints.ReadDetailAsync(conn, userId, activity.Id, ct, tx);
                    var rawFile = activity.RawFileKey is { } key
                        ? $"raw/{activity.Id}{Path.GetExtension(key)}" : null;
                    var efforts = await conn.QueryAsync<StoredBestEffort>(new CommandDefinition("""
                        select activity_id, distance_m, elapsed_s, start_offset_s, achieved_at
                        from best_efforts where activity_id = @id and user_id = @userId order by distance_m
                        """, new { id = activity.Id, userId }, tx, cancellationToken: ct));
                    await WriteJsonAsync(zip, $"activities/{activity.Id}.json", new
                    {
                        activity.Source, activity.CreatedAt, activity.Privacy, activity.StravaActivityId,
                        RawFile = rawFile, Activity = detail, BestEfforts = efforts,
                    }, ct);
                    if (rawFile is not null)
                    {
                        await using var destination = zip.CreateEntry(rawFile).Open();
                        await store.CopyToAsync(activity.RawFileKey!, destination, ct);
                    }
                }
            }
            await tx.CommitAsync(ct);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string path, T value, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(path).Open();
        await JsonSerializer.SerializeAsync(entry, value, JsonSerializerOptions.Web, ct);
    }

    private record ExportFollow(Guid FollowerId, Guid FollowingId, DateTime? AcceptedAt, DateTime CreatedAt);
    private record ExportProfile(Guid Id, string? Email, string DisplayName, DateTime CreatedAt, DateTime? HrConsentAt, string DefaultActivityPrivacy);
    private record ExportActivity(Guid Id, string Source, DateTime CreatedAt, string Privacy,
        string? StravaActivityId, string? RawFileKey);
}
