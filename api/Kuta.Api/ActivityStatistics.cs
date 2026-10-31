using Dapper;
using Npgsql;

public record ActivityStatisticsMonth(
    int Month, int Count, double DurationS, double DistanceM,
    double? PaceSPerKm, double? AvgHr);

public static class ActivityStatistics
{
    public static async Task<IReadOnlyList<ActivityStatisticsMonth>> ReadAsync(
        NpgsqlConnection conn, Guid userId, int year, string timeZone, string? sport, CancellationToken ct)
    {
        var sportFilter = sport is null ? "" : "and sport = @sport";
        return (await conn.QueryAsync<ActivityStatisticsMonth>(new CommandDefinition($"""
            with monthly as (
                select extract(month from started_at at time zone @timeZone)::int as month,
                    count(*)::int as count,
                    sum(duration_s)::double precision as duration_s,
                    sum(distance_m) as distance_m,
                    sum(duration_s::double precision) filter (where distance_m > 0 and duration_s > 0) * 1000
                        / nullif(sum(distance_m) filter (where distance_m > 0 and duration_s > 0), 0) as pace_s_per_km,
                    sum(avg_hr::double precision * duration_s) filter (where avg_hr > 0 and duration_s > 0)
                        / nullif(sum(duration_s::double precision) filter (where avg_hr > 0 and duration_s > 0), 0) as avg_hr
                from activities
                where user_id = @userId
                    and started_at >= (make_date(@year, 1, 1)::timestamp at time zone @timeZone)
                    and started_at < (make_date(@year + 1, 1, 1)::timestamp at time zone @timeZone)
                    {sportFilter}
                group by 1
            )
            select m.month, coalesce(a.count, 0) as count, coalesce(a.duration_s, 0) as duration_s,
                coalesce(a.distance_m, 0) as distance_m, a.pace_s_per_km, a.avg_hr
            from generate_series(1, 12) m(month)
            left join monthly a on a.month = m.month
            order by m.month
            """, new { userId, year, timeZone, sport }, cancellationToken: ct))).ToList();
    }
}
