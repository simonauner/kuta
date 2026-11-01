using Dapper;
using Npgsql;

public static class BestEfforts
{
    // The caller holds the activity lock and transaction, so reprocessing replaces all derived rows atomically.
    // Also refreshes the running intensity behind the training load, which depends on the same streams and parts.
    public static async Task ReplaceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid activityId,
        Guid userId, string sport, DateTime startedAt, int[] t, float?[]? dist, float?[]? alt, ActivityPart[]? parts = null)
    {
        var intensity = dist is null ? (double?)null : TrainingLoad.RunIntensity(sport, t, dist, alt, parts);
        await conn.ExecuteAsync("update activities set run_intensity = @intensity where id = @activityId",
            new { activityId, intensity }, tx);
        await conn.ExecuteAsync("delete from best_efforts where activity_id = @activityId", new { activityId }, tx);
        var efforts = Calculate(sport, t, dist, parts);
        if (efforts.Count == 0) return;
        await conn.ExecuteAsync(
            """
            insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
            values (@activityId, @userId, @DistanceM, @ElapsedS, @StartOffsetS, @achievedAt)
            """,
            efforts.Select(e => new { activityId, userId, e.DistanceM, e.ElapsedS, e.StartOffsetS,
                achievedAt = startedAt.AddSeconds(e.StartOffsetS) }), tx);
    }

    public static IReadOnlyList<BestEffort> Calculate(string sport, int[] t, float?[]? dist, ActivityPart[]? parts = null)
    {
        if (parts is not { Length: > 0 })
            return ActivitySports.IsRunning(sport) ? BestEffortCalculator.Calculate(t, dist) : [];
        return parts.Where(p => ActivitySports.IsRunning(p.Sport)).SelectMany(p =>
        {
            var leg = Multisport.Slice(t, dist, p);
            return BestEffortCalculator.Calculate(leg.T, leg.Dist);
        }).GroupBy(e => e.DistanceM).Select(g => g.OrderBy(e => e.ElapsedS).ThenBy(e => e.StartOffsetS).First())
            .OrderBy(e => e.DistanceM).ToArray();
    }
}
