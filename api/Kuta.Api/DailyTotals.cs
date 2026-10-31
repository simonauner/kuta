using Dapper;
using Npgsql;

public sealed record DailyTotal(DateOnly Day, int Count, double DistanceM, double DurationS);

public static class DailyTotals
{
    // Summor per lokal dag över hela historiken, för jämförelser år för år. Samma regler som målen: hela passet hör
    // till startdagen, en grupp räknar matchande delar av multisportpass, och utan grupp räknas varje pass en gång.
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
