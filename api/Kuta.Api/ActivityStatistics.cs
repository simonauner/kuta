using Dapper;
using Npgsql;

public record ActivityStatisticsMonth(
    int Month, int Count, double DurationS, double DistanceM,
    double? PaceSPerKm, double? AvgHr);

public static class ActivityStatistics
{
    public static async Task<IReadOnlyList<ActivityStatisticsMonth>> ReadAsync(
        NpgsqlConnection conn, Guid userId, int year, string timeZone, string? sportGroup, CancellationToken ct)
    {
        // sportGroup är en nyckel ur SportGroups.All (null = alla). Hela pass i gruppen räknas, och vid gruppfilter även
        // matchande delar av multisportpass i andra grupper.
        return (await conn.QueryAsync<ActivityStatisticsMonth>(new CommandDefinition($"""
            with {SportGroups.Cte}, scoped as (
                select a.* from activities a
                where user_id = @userId
                    and started_at >= (make_date(@year, 1, 1)::timestamp at time zone @timeZone)
                    and started_at < (make_date(@year + 1, 1, 1)::timestamp at time zone @timeZone)
            ), effective as (
                select a.id, a.started_at, a.sport, a.duration_s,
                    coalesce((select sum(p.distance_m) from activity_parts p
                        where p.activity_id = a.id and p.original = not a.parts_edited), a.distance_m) as distance_m,
                    a.avg_hr::double precision as avg_hr
                from scoped a left join sport_groups m on m.sport = a.sport
                where @sportGroup::text is null or coalesce(m.sport_group, a.sport) = @sportGroup
                union all
                select a.id, a.started_at, min(p.sport), sum(p.end_s - p.start_s)::int, sum(p.distance_m),
                    sum(hr.avg_hr * (p.end_s - p.start_s))
                        / nullif(sum(p.end_s - p.start_s) filter (where hr.avg_hr is not null), 0)
                from scoped a join activity_parts p on p.activity_id = a.id and p.original = not a.parts_edited
                left join sport_groups am on am.sport = a.sport
                left join sport_groups pm on pm.sport = p.sport
                left join lateral (
                    select avg(sample.hr::double precision) as avg_hr
                    from activity_streams s, unnest(s.t, s.hr) as sample(t, hr)
                    where s.activity_id = a.id and sample.t >= p.start_s and sample.t < p.end_s and sample.hr > 0
                ) hr on true
                where @sportGroup::text is not null and coalesce(am.sport_group, a.sport) <> @sportGroup
                    and coalesce(pm.sport_group, p.sport) = @sportGroup
                group by a.id, a.started_at
            ), monthly as (
                select extract(month from started_at at time zone @timeZone)::int as month,
                    count(*)::int as count,
                    sum(duration_s)::double precision as duration_s,
                    sum(distance_m) as distance_m,
                    sum(duration_s::double precision) filter (where distance_m > 0 and duration_s > 0 and sport not in ('triathlon', 'swimrun', 'multisport')) * 1000
                        / nullif(sum(distance_m) filter (where distance_m > 0 and duration_s > 0 and sport not in ('triathlon', 'swimrun', 'multisport')), 0) as pace_s_per_km,
                    sum(avg_hr::double precision * duration_s) filter (where avg_hr > 0 and duration_s > 0)
                        / nullif(sum(duration_s::double precision) filter (where avg_hr > 0 and duration_s > 0), 0) as avg_hr
                from effective
                group by 1
            )
            select m.month, coalesce(a.count, 0) as count, coalesce(a.duration_s, 0) as duration_s,
                coalesce(a.distance_m, 0) as distance_m, a.pace_s_per_km, a.avg_hr
            from generate_series(1, 12) m(month)
            left join monthly a on a.month = m.month
            order by m.month
            """, new { userId, year, timeZone, sportGroup, groupedSports = SportGroups.GroupedSports,
                sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct))).ToList();
    }
}
