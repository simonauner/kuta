using Dapper;
using Npgsql;

public static class BestEfforts
{
    // The caller holds the activity lock and transaction, so reprocessing replaces all derived rows atomically.
    public static async Task ReplaceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid activityId,
        Guid userId, string sport, DateTime startedAt, int[] t, float?[]? dist)
    {
        await conn.ExecuteAsync("delete from best_efforts where activity_id = @activityId", new { activityId }, tx);
        if (sport != "running") return;
        var efforts = BestEffortCalculator.Calculate(t, dist);
        if (efforts.Count == 0) return;
        await conn.ExecuteAsync(
            """
            insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
            values (@activityId, @userId, @DistanceM, @ElapsedS, @StartOffsetS, @achievedAt)
            """,
            efforts.Select(e => new { activityId, userId, e.DistanceM, e.ElapsedS, e.StartOffsetS,
                achievedAt = startedAt.AddSeconds(e.StartOffsetS) }), tx);
    }
}
