using Dapper;
using Npgsql;

public sealed record DailyTotal(DateOnly Day, int Count, double DistanceM, double DurationS);

public static class DailyTotals
{
    // Totals per local day over the whole history, for year-over-year comparisons. Same rules as goals: the whole activity belongs
    // to its start day, a group counts matching parts of multisport activities, and without a group each activity counts once.
    public static async Task<IReadOnlyList<DailyTotal>> ReadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        string? sportGroup, CancellationToken ct) =>
        (await conn.QueryAsync<DailyTotal>(new CommandDefinition($"""
            with {SportGroups.Cte}, {SportGroups.EffectiveCte}
            select (started_at at time zone @timeZone)::date as day, count(distinct id)::int as count,
                coalesce(sum(distance_m), 0) as distance_m, coalesce(sum(duration_s), 0) as duration_s
            from effective
            where case when @sportGroup::text is null then not part else sport_group = @sportGroup end
            group by 1
            order by 1
            """, new { userId, timeZone, sportGroup, groupedSports = SportGroups.GroupedSports,
                sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct))).ToList();
}
