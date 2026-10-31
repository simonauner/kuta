using Dapper;
using Npgsql;

public sealed record RecordMonth(int Year, int Month, Guid ActivityId, double ElapsedS, DateTime AchievedAt);

public sealed record RecordTrend(IReadOnlyList<RecordMonth> Months, IReadOnlyList<StoredBestEffort> Progression);

public static class RecordTrends
{
    // Bästa insats per lokal kalendermånad (bara månader med insatser) och rekordhistoriken: varje insats som var
    // strikt snabbare än alla tidigare. Lika tid behåller det äldre rekordet, som personbästa gör.
    public static async Task<RecordTrend> ReadAsync(NpgsqlConnection conn, Guid userId, int distanceM, string timeZone,
        CancellationToken ct)
    {
        var args = new { userId, distanceM, timeZone };
        var months = await conn.QueryAsync<RecordMonth>(new CommandDefinition("""
            select distinct on (year, month) year, month, activity_id, elapsed_s, achieved_at
            from (
                select extract(year from achieved_at at time zone @timeZone)::int as year,
                    extract(month from achieved_at at time zone @timeZone)::int as month, *
                from best_efforts where user_id = @userId and distance_m = @distanceM
                  and activity_id in (select id from activities where user_id = @userId and not exclude_from_records)
            ) e
            order by year, month, elapsed_s, achieved_at, activity_id
            """, args, cancellationToken: ct));
        var progression = await conn.QueryAsync<StoredBestEffort>(new CommandDefinition("""
            select activity_id, distance_m, elapsed_s, start_offset_s, achieved_at
            from (
                select *, min(elapsed_s) over (order by achieved_at, activity_id
                    rows between unbounded preceding and 1 preceding) as previous_best
                from best_efforts where user_id = @userId and distance_m = @distanceM
                  and activity_id in (select id from activities where user_id = @userId and not exclude_from_records)
            ) e
            where previous_best is null or elapsed_s < previous_best
            order by achieved_at, activity_id
            """, args, cancellationToken: ct));
        return new RecordTrend(months.ToList(), progression.ToList());
    }

    public static async Task<RacePredictions> PredictAsync(NpgsqlConnection conn, Guid userId, DateTime now,
        CancellationToken ct)
    {
        var efforts = await conn.QueryAsync<StoredBestEffort>(new CommandDefinition("""
            select distinct on (distance_m) activity_id, distance_m, elapsed_s, start_offset_s, achieved_at
            from best_efforts
            where user_id = @userId
              and activity_id in (select id from activities where user_id = @userId and not exclude_from_records)
              and distance_m >= @minimum and achieved_at >= @since and achieved_at <= @now
            order by distance_m, elapsed_s, achieved_at desc, activity_id
            """, new { userId, minimum = RacePredictor.MinimumBasisDistanceM, since = now - RacePredictor.Window, now },
            cancellationToken: ct));
        return RacePredictor.Calculate(efforts);
    }
}
