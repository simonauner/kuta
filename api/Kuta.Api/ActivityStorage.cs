using System.Security.Cryptography;
using Dapper;
using Npgsql;

public static class ActivityStorage
{
    public static async Task<bool> DeleteAsync(NpgsqlConnection conn, RawFileStore store, Guid userId, Guid id,
        CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        var activity = await conn.QuerySingleOrDefaultAsync<OwnedFile>(new CommandDefinition(
            "select id, raw_file_key from activities where id = @id and user_id = @userId for update",
            new { id, userId }, tx, cancellationToken: ct));
        if (activity is null) return false;
        if (activity.RawFileKey is { } key) await store.DeleteAsync([key], ct);
        await conn.ExecuteAsync(new CommandDefinition("delete from activities where id = @id and user_id = @userId",
            new { id, userId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    private record OwnedFile(Guid Id, string? RawFileKey);

    public static async Task<ActivitySummary?> SaveManualAsync(NpgsqlConnection conn, Guid userId,
        ManualActivityRequest req, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Same profile lock as consent withdrawal: never read an older consent before the transaction.
        var profile = await conn.QuerySingleOrDefaultAsync<Profile>(new CommandDefinition(
            "select display_name, hr_consent_at is not null as hr_consent, default_activity_privacy from users where id = @userId for update",
            new { userId }, tx, cancellationToken: ct));
        if (profile is null) return null;
        var equipmentIds = req.EquipmentIds ?? [];
        if (!await EquipmentEndpoints.OwnsAsync(conn, tx, userId, equipmentIds))
            throw new InvalidEquipmentException();
        var title = ActivityInput.NormalizeTitle(req.Title);
        var startedAt = req.StartedAt!.Value.UtcDateTime;
        var avgHr = profile.HrConsent ? req.AvgHr : null;
        var tags = ActivityInput.NormalizeTags(req.Tags);
        var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            insert into activities (user_id, source, sport, title, started_at, duration_s, distance_m, avg_hr,
                                    avg_speed_mps, elevation_gain_m, tags, description, privacy)
            values (@userId, 'manual', @Sport, @title, @startedAt, @DurationS, @DistanceM, @avgHr,
                    @avgSpeedMps, @ElevationGainM, @tags, @description, @DefaultActivityPrivacy)
            returning id
            """, new
            {
                userId, req.Sport, title, startedAt, req.DurationS, req.DistanceM, avgHr, tags, profile.DefaultActivityPrivacy,
                description = ActivityInput.NormalizeDescription(req.Description),
                avgSpeedMps = (float)(req.DistanceM!.Value / req.DurationS!.Value), req.ElevationGainM,
            }, tx, cancellationToken: ct));
        await EquipmentEndpoints.AttachAsync(conn, tx, id, userId, equipmentIds);
        await tx.CommitAsync(ct);
        return new ActivitySummary(id, req.Sport!, title, startedAt, req.DurationS!.Value, req.DistanceM!.Value,
            avgHr, tags, equipmentIds.Distinct().ToArray());
    }

    // The same lock is used for consent withdrawal, so a long import cannot save heart rate afterwards.
    public static async Task<Guid?> SaveAsync(NpgsqlConnection conn, RawFileStore store, Guid userId,
        ParsedActivity parsed, string source, string? title, string? stravaId, byte[]? bytes, string extension,
        CancellationToken ct, string[]? tags = null, Guid[]? equipmentIds = null, string? description = null, MultisportRequest? split = null)
    {
        var hash = bytes is null ? null : SHA256.HashData(bytes);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (!await EquipmentEndpoints.OwnsAsync(conn, tx, userId, equipmentIds ?? [], allowRetired: source == "strava"))
            throw new InvalidEquipmentException();
        var profile = await conn.QuerySingleAsync<Profile>(
            "select display_name, hr_consent_at is not null as hr_consent, default_activity_privacy from users where id = @userId for update", new { userId }, tx);
        if (!profile.HrConsent) parsed = parsed.WithoutHeartRate();
        var key = profile.HrConsent && hash is not null ? $"fit/{userId}/{Convert.ToHexStringLower(hash)}{extension}" : null;
        var id = await conn.QuerySingleOrDefaultAsync<Guid?>(
            """
            insert into activities (user_id, source, sport, title, started_at, duration_s, moving_s, distance_m,
                                    avg_hr, max_hr, avg_speed_mps, elevation_gain_m, route, device, raw_file_key, raw_file_sha256, strava_activity_id, tags, description, privacy)
            values (@userId, @source, @Sport, @title, @StartedAt, @DurationS, @MovingS, @DistanceM,
                    @AvgHr, @MaxHr, @AvgSpeedMps, @ElevationGainM, @route::geography, @Device, @key, @hash, @stravaId, @tags, @description, @privacy)
            on conflict do nothing returning id
            """,
            new
            {
                userId, source, title, stravaId, description = ActivityInput.NormalizeDescription(description), parsed.Sport, parsed.StartedAt, parsed.DurationS, parsed.MovingS,
                parsed.DistanceM, parsed.AvgHr, parsed.MaxHr, parsed.AvgSpeedMps, parsed.ElevationGainM,
                route = (object?)parsed.Route ?? DBNull.Value, parsed.Device, key, hash, tags = ActivityInput.NormalizeTags(tags),
                // Garmin activities are private until Garmin approves showing them to others (constraint in migration 025).
                privacy = source == "garmin" ? "private" : profile.DefaultActivityPrivacy,
            }, tx);
        if (id is null) return null;
        await conn.ExecuteAsync(
            """
            insert into activity_streams (activity_id, t, dist, hr, speed, alt, lat, lon, cadence, temp)
            values (@id, @T, @Dist, @Hr, @Speed, @Alt, @Lat, @Lon, @Cadence, @Temp)
            """, new { id, parsed.T, parsed.Dist, parsed.Hr, parsed.Speed, parsed.Alt, parsed.Lat, parsed.Lon, parsed.Cadence, parsed.Temp }, tx);
        await conn.ExecuteAsync(
            """
            insert into activity_laps (activity_id, lap_index, start_s, duration_s, moving_s, distance_m, avg_hr, max_hr,
                                       ascent_m, descent_m, avg_cadence)
            values (@id, @LapIndex, @StartS, @DurationS, @MovingS, @DistanceM, @AvgHr, @MaxHr,
                    @AscentM, @DescentM, @AvgCadence)
            """,
            (parsed.Laps ?? []).Select((l, i) => new
            {
                id, LapIndex = (short)i, l.StartS, l.DurationS, l.MovingS, l.DistanceM, l.AvgHr, l.MaxHr,
                l.AscentM, l.DescentM, l.AvgCadence,
            }), tx);
        await EquipmentEndpoints.AttachAsync(conn, tx, id.Value, userId, equipmentIds ?? []);
        if (parsed.Parts is { Length: > 0 } parts)
            await Multisport.SavePartsAsync(conn, tx, id.Value, parts, true);
        if (split is not null)
        {
            if (await Multisport.ApplyAsync(conn, tx, userId, id.Value, split) != 204)
                throw new InvalidOperationException("Invalid activity parts.");
        }
        else await BestEfforts.ReplaceAsync(conn, tx, id.Value, userId, parsed.Sport, parsed.StartedAt, parsed.T, parsed.Dist, parsed.Alt, parsed.Parts);
        try
        {
            if (key is not null) await store.PutAsync(key, bytes!, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.DisposeAsync();
            if (key is not null) await CleanupUnreferencedFileAsync(conn, store, userId, key);
            throw;
        }
        return id;
    }

    public static async Task CleanupUnreferencedFileAsync(NpgsqlConnection failedConnection, RawFileStore store,
        Guid userId, string key)
    {
        // A lost commit response does not mean rollback. Check the outcome before deleting the file.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var conn = failedConnection.CloneWith(failedConnection.ConnectionString);
        await conn.OpenAsync(completion.Token);
        await using var tx = await conn.BeginTransactionAsync(completion.Token);
        // Prevent a new import from reusing the key between the check and the deletion.
        await conn.ExecuteAsync(new CommandDefinition("select id from users where id = @userId for update",
            new { userId }, tx, cancellationToken: completion.Token));
        var referenced = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from activities where user_id = @userId and raw_file_key = @key)",
            new { userId, key }, tx, cancellationToken: completion.Token));
        if (!referenced) await store.DeleteAsync([key], completion.Token);
    }

}
