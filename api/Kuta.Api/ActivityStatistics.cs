using Dapper;
using Npgsql;

public record ActivityStatisticsBucket(
    DateOnly Start, int Count, double DurationS, double DistanceM,
    double? PaceSPerKm, double? AvgHr);

public record ActivityStatisticsPeriod(IReadOnlyList<ActivityStatisticsBucket> Buckets, ActivityStatisticsBucket Total);

public static class ActivityStatistics
{
    public static readonly string[] Buckets = ["week", "month"];

    private sealed record Row(DateOnly? Start, int Count, double DurationS, double DistanceM, double? PaceSPerKm, double? AvgHr);

    // Local dates [from, to) split into ISO weeks or calendar months (bucket from Buckets), plus the total for the whole range.
    public static async Task<ActivityStatisticsPeriod> ReadAsync(NpgsqlConnection conn, Guid userId, DateOnly from, DateOnly to,
        string bucket, string timeZone, string? sportGroup, CancellationToken ct)
    {
        // sportGroup is a key from SportGroups.All (null = all). Whole activities in the group count, and with a group filter also
        // matching parts of multisport activities in other groups.
        var rows = (await conn.QueryAsync<Row>(new CommandDefinition($"""
            with {SportGroups.Cte}, scoped as (
                select a.* from activities a
                where user_id = @userId
                    and started_at >= (@from::timestamp at time zone @timeZone)
                    and started_at < (@to::timestamp at time zone @timeZone)
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
            ), bucketed as (
                select date_trunc(@bucket, started_at at time zone @timeZone)::date as start, * from effective
            ), totals as (
                select start, grouping(start) = 1 as total,
                    count(*)::int as count,
                    sum(duration_s)::double precision as duration_s,
                    sum(distance_m) as distance_m,
                    sum(duration_s::double precision) filter (where distance_m > 0 and duration_s > 0 and sport not in ('triathlon', 'swimrun', 'multisport')) * 1000
                        / nullif(sum(distance_m) filter (where distance_m > 0 and duration_s > 0 and sport not in ('triathlon', 'swimrun', 'multisport')), 0) as pace_s_per_km,
                    sum(avg_hr::double precision * duration_s) filter (where avg_hr > 0 and duration_s > 0)
                        / nullif(sum(duration_s::double precision) filter (where avg_hr > 0 and duration_s > 0), 0) as avg_hr
                from bucketed
                group by grouping sets ((start), ())
            )
            select b.start::date as start, coalesce(a.count, 0) as count, coalesce(a.duration_s, 0) as duration_s,
                coalesce(a.distance_m, 0) as distance_m, a.pace_s_per_km, a.avg_hr
            from generate_series(date_trunc(@bucket, @from::timestamp), @to::timestamp - interval '1 day',
                ('1 ' || @bucket)::interval) b(start)
            left join totals a on a.start = b.start::date and not a.total
            union all
            select null, count, duration_s, distance_m, pace_s_per_km, avg_hr from totals where total
            order by 1 nulls last
            """, new { userId, from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue), bucket, timeZone,
                sportGroup, groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys },
            cancellationToken: ct))).ToList();
        var total = rows.FirstOrDefault(r => r.Start is null);
        return new(
            rows.Where(r => r.Start is not null)
                .Select(r => new ActivityStatisticsBucket(r.Start!.Value, r.Count, r.DurationS, r.DistanceM, r.PaceSPerKm, r.AvgHr))
                .ToList(),
            new(from, total?.Count ?? 0, total?.DurationS ?? 0, total?.DistanceM ?? 0, total?.PaceSPerKm, total?.AvgHr));
    }
}
