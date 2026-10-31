using System.Security.Cryptography;
using Dapper;
using Npgsql;

public static class ActivityStorage
{
    // Samma lås används vid återkallat samtycke, så att en lång import inte kan spara puls efteråt.
    public static async Task<Guid?> SaveAsync(NpgsqlConnection conn, RawFileStore store, Guid userId,
        ParsedActivity parsed, string source, string? title, string? stravaId, byte[]? bytes, string extension,
        CancellationToken ct, string[]? tags = null, Guid[]? equipmentIds = null)
    {
        var hash = bytes is null ? null : SHA256.HashData(bytes);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (!await EquipmentEndpoints.OwnsAsync(conn, tx, userId, equipmentIds ?? []))
            throw new InvalidEquipmentException();
        var consent = await conn.QuerySingleAsync<bool>(
            "select hr_consent_at is not null from users where id = @userId for update", new { userId }, tx);
        if (!consent) parsed = parsed.WithoutHeartRate();
        var key = consent && hash is not null ? $"fit/{userId}/{Convert.ToHexStringLower(hash)}{extension}" : null;
        var id = await conn.QuerySingleOrDefaultAsync<Guid?>(
            """
            insert into activities (user_id, source, sport, title, started_at, duration_s, moving_s, distance_m,
                                    avg_hr, max_hr, avg_speed_mps, elevation_gain_m, route, device, raw_file_key, raw_file_sha256, strava_activity_id, tags)
            values (@userId, @source, @Sport, @title, @StartedAt, @DurationS, @MovingS, @DistanceM,
                    @AvgHr, @MaxHr, @AvgSpeedMps, @ElevationGainM, @route::geography, @Device, @key, @hash, @stravaId, @tags)
            on conflict do nothing returning id
            """,
            new
            {
                userId, source, title, stravaId, parsed.Sport, parsed.StartedAt, parsed.DurationS, parsed.MovingS,
                parsed.DistanceM, parsed.AvgHr, parsed.MaxHr, parsed.AvgSpeedMps, parsed.ElevationGainM,
                route = (object?)parsed.Route ?? DBNull.Value, parsed.Device, key, hash, tags = ActivityInput.NormalizeTags(tags),
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
        await BestEfforts.ReplaceAsync(conn, tx, id.Value, userId, parsed.Sport, parsed.StartedAt, parsed.T, parsed.Dist);
        try
        {
            if (key is not null) await store.PutAsync(key, bytes!, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (key is not null) await store.DeleteAsync([key], CancellationToken.None);
            throw;
        }
        return id;
    }
}
