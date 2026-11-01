using Dapper;
using Dynastream.Fit;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

// Push payload from Garmin (one list per summary type; unknown types are ignored).
public sealed record GarminPush(GarminFileNotice[]? ActivityFiles, GarminUserRef[]? Deregistrations,
    GarminPermissionChange[]? UserPermissionsChange);
public sealed record GarminFileNotice(string? UserId, string? SummaryId, string? FileType, string? CallbackUrl,
    string? DeviceName, string? ActivityName, string? ActivityDescription);
public sealed record GarminUserRef(string? UserId);
public sealed record GarminPermissionChange(string? UserId, string[]? Permissions);
public sealed record GarminImportResult(int Imported, int Skipped);

public static class Garmin
{
    static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("kuta.garmin-tokens.v1");

    sealed record StoredTokens(string AccessToken, string RefreshToken, System.DateTime AccessExpiresAt);
    sealed record PendingFile(string SummaryId, string CallbackUrl, string FileType, string? DeviceName,
        string? ActivityName, string? ActivityDescription, System.DateTime ReceivedAt);

    // false when the Garmin account is already connected to another Kuta user.
    public static async Task<bool> SaveConnectionAsync(NpgsqlConnection conn, IDataProtectionProvider provider,
        Guid userId, string garminUserId, GarminTokens tokens, string[] permissions, CancellationToken ct)
    {
        var protector = Protector(provider);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                insert into garmin_connections (user_id, garmin_user_id, access_token, refresh_token, access_expires_at,
                    refresh_expires_at, permissions)
                values (@userId, @garminUserId, @access, @refresh, @AccessExpiresAt, @RefreshExpiresAt, @permissions)
                on conflict (user_id) do update set garmin_user_id = excluded.garmin_user_id,
                    access_token = excluded.access_token, refresh_token = excluded.refresh_token,
                    access_expires_at = excluded.access_expires_at, refresh_expires_at = excluded.refresh_expires_at,
                    permissions = excluded.permissions
                """, new
                {
                    userId, garminUserId, access = protector.Protect(tokens.AccessToken),
                    refresh = protector.Protect(tokens.RefreshToken), tokens.AccessExpiresAt, tokens.RefreshExpiresAt,
                    permissions,
                }, cancellationToken: ct));
            return true;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    // A valid access token, refreshed when needed under a row lock so two refreshes do not collide. null when there is
    // no connection or Garmin has revoked it.
    public static async Task<string?> AccessTokenAsync(NpgsqlConnection conn, GarminClient garmin,
        IDataProtectionProvider provider, Guid userId, CancellationToken ct, bool forceRefresh = false)
    {
        var protector = Protector(provider);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var stored = await conn.QuerySingleOrDefaultAsync<StoredTokens>(new CommandDefinition("""
            select access_token, refresh_token, access_expires_at from garmin_connections where user_id = @userId for update
            """, new { userId }, tx, cancellationToken: ct));
        if (stored is null) return null;
        if (!forceRefresh && stored.AccessExpiresAt > System.DateTime.UtcNow) return protector.Unprotect(stored.AccessToken);
        var tokens = await garmin.RefreshAsync(protector.Unprotect(stored.RefreshToken), ct);
        if (tokens is null) return null;
        await conn.ExecuteAsync(new CommandDefinition("""
            update garmin_connections set access_token = @access, refresh_token = @refresh,
                access_expires_at = @AccessExpiresAt, refresh_expires_at = @RefreshExpiresAt
            where user_id = @userId
            """, new
            {
                userId, access = protector.Protect(tokens.AccessToken), refresh = protector.Protect(tokens.RefreshToken),
                tokens.AccessExpiresAt, tokens.RefreshExpiresAt,
            }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return tokens.AccessToken;
    }

    // Stores the notices quickly (Garmin requires a response within 30 s) and leaves downloading to the job queue.
    public static async Task HandlePushAsync(NpgsqlConnection conn, RawFileStore store, GarminPush push, CancellationToken ct)
    {
        foreach (var file in push.ActivityFiles ?? [])
        {
            if (file is not { UserId: { } garminUserId, SummaryId: { } summaryId, CallbackUrl: { } url, FileType: { } type }
                || !GarminClient.IsGarminUrl(url)) continue;
            var userId = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                insert into garmin_files (summary_id, user_id, callback_url, file_type, device_name, activity_name,
                    activity_description)
                select @summaryId, user_id, @url, @type, @DeviceName, @ActivityName, @ActivityDescription
                from garmin_connections where garmin_user_id = @garminUserId
                on conflict (summary_id) do nothing
                returning user_id
                """, new { summaryId, url, type, file.DeviceName, file.ActivityName, file.ActivityDescription, garminUserId },
                cancellationToken: ct));
            if (userId is { } id) await JobQueue.EnqueueAsync(conn, id, JobKinds.GarminImport, ct: ct);
        }
        foreach (var change in push.UserPermissionsChange ?? [])
            if (change.UserId is { } garminUserId)
                await conn.ExecuteAsync(new CommandDefinition(
                    "update garmin_connections set permissions = @permissions where garmin_user_id = @garminUserId",
                    new { garminUserId, permissions = change.Permissions ?? [] }, cancellationToken: ct));
        foreach (var deregistration in push.Deregistrations ?? [])
        {
            var userId = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "select user_id from garmin_connections where garmin_user_id = @UserId", deregistration, cancellationToken: ct));
            // Privacy policy: disconnecting at Garmin also deletes what was retrieved from it.
            // shortcut: deletes raw files before answering Garmin; move to the job queue if this approaches 30 s.
            if (userId is { } id) await DeleteDataAsync(conn, store, id, ct);
        }
    }

    // Removes the connection, pending files and all Garmin activities. Raw files are deleted first, under the same
    // profile lock as the import, so no new ones can be saved in between.
    public static async Task DeleteDataAsync(NpgsqlConnection conn, RawFileStore store, Guid userId, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("select id from users where id = @userId for update",
            new { userId }, tx, cancellationToken: ct));
        var keys = await conn.QueryAsync<string>(new CommandDefinition("""
            select raw_file_key from activities where user_id = @userId and source = 'garmin' and raw_file_key is not null
            """, new { userId }, tx, cancellationToken: ct));
        await store.DeleteAsync(keys, ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            delete from activities where user_id = @userId and source = 'garmin';
            delete from garmin_files where user_id = @userId;
            delete from garmin_connections where user_id = @userId;
            """, new { userId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    // Deregisters at Garmin (required when the user disconnects or deletes the account in Kuta). Garmin errors do not
    // stop the local deletion; Garmin may then send data that Kuta ignores because the connection is gone.
    public static async Task DeregisterAsync(NpgsqlConnection conn, GarminClient garmin, IDataProtectionProvider provider,
        Guid userId, CancellationToken ct)
    {
        if (!garmin.Configured) return;
        try
        {
            if (await AccessTokenAsync(conn, garmin, provider, userId, ct) is { } token)
                await garmin.DeleteRegistrationAsync(token, ct);
        }
        catch (Exception e) when (e is GarminUnavailableException or HttpRequestException
            or System.Security.Cryptography.CryptographicException) { }
    }

    public static async Task<GarminImportResult> RunImportAsync(NpgsqlConnection conn, RawFileStore store,
        GarminClient garmin, IDataProtectionProvider provider, JobRow job, CancellationToken ct)
    {
        int imported = 0, skipped = 0;
        while (await conn.QuerySingleOrDefaultAsync<PendingFile>(new CommandDefinition("""
            select summary_id, callback_url, file_type, device_name, activity_name, activity_description, received_at
            from garmin_files where user_id = @UserId order by received_at, summary_id limit 1
            """, job, cancellationToken: ct)) is { } file)
        {
            if (await ImportAsync(conn, store, garmin, provider, job.UserId, file, ct)) imported++;
            else skipped++;
            await conn.ExecuteAsync(new CommandDefinition("delete from garmin_files where summary_id = @SummaryId",
                file, cancellationToken: ct));
            await JobQueue.HeartbeatAsync(conn, job.Id, null, ct);
        }
        return new GarminImportResult(imported, skipped);
    }

    static async Task<bool> ImportAsync(NpgsqlConnection conn, RawFileStore store, GarminClient garmin,
        IDataProtectionProvider provider, Guid userId, PendingFile file, CancellationToken ct)
    {
        // shortcut: FIT only (Garmin watches almost always send FIT); add GPX via StravaImport if needed.
        if (file.FileType != "FIT" || file.ReceivedAt < System.DateTime.UtcNow.AddHours(-24)) return false;
        if (await AccessTokenAsync(conn, garmin, provider, userId, ct) is not { } token) return false;
        if (await garmin.DownloadAsync(file.CallbackUrl, token, ct) is not { } bytes) return false;
        ParsedActivity? parsed;
        // As in the Strava import: the SDK throws more than FitException, and a file that always fails would stay first in
        // the queue and block the user's later files until they expire.
        try { parsed = FitImport.Parse(bytes); }
        catch (Exception e) when (e is not OperationCanceledException) { parsed = null; }
        if (parsed is null) return false;
        // Garmin's attribution should show the device name from the API; manual activities have 'unknown'.
        if (file.DeviceName is { Length: > 0 } device and not "unknown") parsed = parsed with { Device = device };
        // Like the activity form: runs get the last used active shoe, so shoe distance is right even if never edited.
        Guid[] equipment = SportGroups.GroupOf(parsed.Sport) == "running"
            && await EquipmentEndpoints.LastUsedShoeAsync(conn, userId) is { } shoe ? [shoe] : [];
        return await ActivityStorage.SaveAsync(conn, store, userId, parsed, "garmin", ActivityInput.NormalizeTitle(file.ActivityName), null, bytes,
            ".fit", ct, equipmentIds: equipment, description: file.ActivityDescription) is not null;
    }

    public static async Task EnqueueIfPendingAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct)
    {
        if (await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists (select 1 from garmin_files where user_id = @userId)", new { userId }, cancellationToken: ct)))
            await JobQueue.EnqueueAsync(conn, userId, JobKinds.GarminImport, ct: ct);
    }

    // Hourly: drop notices whose callback URL has expired and refresh the refresh token (valid three months) for
    // connections without new data, so deregistration on disconnect keeps working.
    public static async Task MaintainAsync(NpgsqlConnection conn, GarminClient garmin, IDataProtectionProvider provider,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from garmin_files where received_at < now() - interval '24 hours'", cancellationToken: ct));
        if (!garmin.Configured) return;
        var expiring = await conn.QueryAsync<Guid>(new CommandDefinition(
            "select user_id from garmin_connections where refresh_expires_at < now() + interval '14 days'",
            cancellationToken: ct));
        foreach (var userId in expiring)
        {
            try { await AccessTokenAsync(conn, garmin, provider, userId, ct, forceRefresh: true); }
            catch (GarminUnavailableException) { }
        }
    }
}
