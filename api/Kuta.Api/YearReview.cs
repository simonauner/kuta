using System.Security.Claims;
using Dapper;
using Npgsql;

// One row per own activity (Part = false) and per other sport group in a multisport activity, as SportGroups.EffectiveCte.
public sealed record ReviewRow(Guid Id, DateTime StartedAt, DateTime LocalStart, string SportGroup, string Sport,
    string? Title, double DistanceM, double DurationS, double ElevationM, bool Part, bool Race);

public sealed record YearReviewTotals(int Count, double DistanceM, double DurationS, double ElevationM, int ActiveDays);
public sealed record YearReviewSport(string SportGroup, int Count, double DistanceM, double DurationS);
public sealed record YearReviewMonth(int Month, int Count, double DistanceM, double DurationS);
public sealed record YearReviewActivity(Guid ActivityId, string Sport, string? Title, DateTime StartedAt,
    double DistanceM, double DurationS);
public sealed record YearReviewRecord(int DistanceM, double ElapsedS, Guid ActivityId, DateTime AchievedAt, bool PersonalBest);
public sealed record YearReviewEquipment(Guid Id, string Name, int Count, double DistanceM);

// Weekday 1 = Monday; TimeOfDay is morning (05–10), midday (10–14), afternoon (14–18), evening (18–22) or night.
public sealed record YearReviewSummary(
    YearReviewTotals Totals, YearReviewSport[] Sports, YearReviewMonth[] Months, int? FavoriteWeekday,
    int FavoriteWeekdayCount, string? TimeOfDay, int TimeOfDayCount, int LongestDayStreak, int LongestWeekStreak,
    YearReviewActivity? LongestRun, YearReviewActivity? LongestActivity, YearReviewActivity[] Races);

public sealed record YearReviewResult(int Year, bool Complete, YearReviewSummary Summary, YearReviewTotals? Previous,
    YearReviewRecord[] Records, YearReviewEquipment? TopEquipment);

// The year in review: fixed summaries of one calendar year in the viewer's time zone, own activities only.
public static class YearReview
{
    const string Running = "running";

    public static YearReviewTotals Totals(IReadOnlyList<ReviewRow> rows)
    {
        var activities = rows.Where(r => !r.Part).ToList();
        return new YearReviewTotals(activities.Count, activities.Sum(a => a.DistanceM), activities.Sum(a => a.DurationS),
            activities.Sum(a => Math.Max(0, a.ElevationM)),
            activities.Select(a => DateOnly.FromDateTime(a.LocalStart)).Distinct().Count());
    }

    public static string TimeOfDay(int hour) => hour switch
    {
        >= 5 and < 10 => "morning",
        >= 10 and < 14 => "midday",
        >= 14 and < 18 => "afternoon",
        >= 18 and < 22 => "evening",
        _ => "night",
    };

    static int Longest(IEnumerable<DateOnly> dates, int step)
    {
        int longest = 0, current = 0;
        DateOnly? previous = null;
        foreach (var date in dates.Distinct().Order())
        {
            current = previous?.AddDays(step) == date ? current + 1 : 1;
            longest = Math.Max(longest, current);
            previous = date;
        }
        return longest;
    }

    static YearReviewActivity Activity(ReviewRow row) =>
        new(row.Id, row.Sport, row.Title, row.StartedAt, row.DistanceM, row.DurationS);

    public static YearReviewSummary Summarize(IReadOnlyList<ReviewRow> rows)
    {
        var activities = rows.Where(r => !r.Part).OrderBy(r => r.StartedAt).ThenBy(r => r.Id).ToList();
        var sports = rows.GroupBy(r => r.SportGroup)
            .Select(g => new YearReviewSport(g.Key, g.Select(r => r.Id).Distinct().Count(), g.Sum(r => r.DistanceM),
                g.Sum(r => r.DurationS)))
            .OrderByDescending(s => s.DurationS).ThenBy(s => s.SportGroup, StringComparer.Ordinal).ToArray();
        var months = Enumerable.Range(1, 12).Select(m =>
        {
            var inMonth = activities.Where(a => a.LocalStart.Month == m).ToList();
            return new YearReviewMonth(m, inMonth.Count, inMonth.Sum(a => a.DistanceM), inMonth.Sum(a => a.DurationS));
        }).ToArray();
        // Ties go to the earlier weekday or time of day, so the answer doesn't change between requests
        var weekday = activities.GroupBy(a => ((int)a.LocalStart.DayOfWeek + 6) % 7 + 1)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
        string[] times = ["morning", "midday", "afternoon", "evening", "night"];
        var time = activities.GroupBy(a => TimeOfDay(a.LocalStart.Hour))
            .OrderByDescending(g => g.Count()).ThenBy(g => Array.IndexOf(times, g.Key)).FirstOrDefault();
        var days = activities.Select(a => DateOnly.FromDateTime(a.LocalStart)).ToList();
        var longestRun = rows.Where(r => r.SportGroup == Running && r.DistanceM > 0)
            .OrderByDescending(r => r.DistanceM).ThenBy(r => r.StartedAt).FirstOrDefault();
        var longestActivity = activities.OrderByDescending(a => a.DurationS).ThenBy(a => a.StartedAt).FirstOrDefault();
        return new YearReviewSummary(Totals(rows), sports, months, weekday?.Key, weekday?.Count() ?? 0,
            time?.Key, time?.Count() ?? 0, Longest(days, 1),
            Longest(days.Select(d => d.AddDays(-(((int)d.DayOfWeek + 6) % 7))), 7),
            longestRun is null ? null : Activity(longestRun),
            longestActivity is null ? null : Activity(longestActivity),
            activities.Where(a => a.Race).Select(Activity).ToArray());
    }

    static Task<IEnumerable<ReviewRow>> RowsAsync(NpgsqlConnection conn, Guid userId, int year, string timeZone,
        CancellationToken ct) =>
        conn.QueryAsync<ReviewRow>(new CommandDefinition($"""
            with {SportGroups.Cte}, {SportGroups.EffectiveCte}
            select e.id, e.started_at, e.started_at at time zone @timeZone as local_start, e.sport_group, a.sport, a.title,
                e.distance_m::double precision as distance_m, e.duration_s,
                coalesce(a.elevation_gain_m, 0)::double precision as elevation_m, e.part, 'race' = any(a.tags) as race
            from effective e join activities a on a.id = e.id
            where e.started_at >= make_timestamp(@year, 1, 1, 0, 0, 0) at time zone @timeZone
                and e.started_at < make_timestamp(@year + 1, 1, 1, 0, 0, 0) at time zone @timeZone
            """, new { userId, year, timeZone, groupedSports = SportGroups.GroupedSports,
                sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct));

    public static async Task<YearReviewResult> ReadAsync(NpgsqlConnection conn, Guid userId, int year, string timeZone,
        DateTime now, CancellationToken ct)
    {
        var rows = (await RowsAsync(conn, userId, year, timeZone, ct)).ToList();
        var previous = Totals((await RowsAsync(conn, userId, year - 1, timeZone, ct)).ToList());
        var currentYear = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select extract(year from @now::timestamptz at time zone @timeZone)::int", new { now, timeZone },
            cancellationToken: ct));
        var bounds = new { userId, year, timeZone };
        // The year's best per distance, and whether it beat everything before it
        var records = await conn.QueryAsync<YearReviewRecord>(new CommandDefinition("""
            with year_best as (
                select distinct on (b.distance_m) b.distance_m, b.elapsed_s::double precision as elapsed_s,
                    b.activity_id, b.achieved_at
                from best_efforts b join activities a on a.id = b.activity_id
                where b.user_id = @userId and not a.exclude_from_records
                    and b.achieved_at >= make_timestamp(@year, 1, 1, 0, 0, 0) at time zone @timeZone
                    and b.achieved_at < make_timestamp(@year + 1, 1, 1, 0, 0, 0) at time zone @timeZone
                order by b.distance_m, b.elapsed_s, b.achieved_at
            )
            select y.distance_m, y.elapsed_s, y.activity_id, y.achieved_at,
                not exists (select 1 from best_efforts p join activities pa on pa.id = p.activity_id
                    where p.user_id = @userId and not pa.exclude_from_records and p.distance_m = y.distance_m
                        and p.achieved_at < y.achieved_at and p.elapsed_s <= y.elapsed_s) as personal_best
            from year_best y order by y.distance_m
            """, bounds, cancellationToken: ct));
        var equipment = await conn.QuerySingleOrDefaultAsync<YearReviewEquipment>(new CommandDefinition("""
            select e.id, e.name, count(*)::int as count, sum(a.distance_m)::double precision as distance_m
            from activity_equipment ae
            join equipment e on e.id = ae.equipment_id
            join activities a on a.id = ae.activity_id
            where a.user_id = @userId
                and a.started_at >= make_timestamp(@year, 1, 1, 0, 0, 0) at time zone @timeZone
                and a.started_at < make_timestamp(@year + 1, 1, 1, 0, 0, 0) at time zone @timeZone
            group by e.id, e.name
            order by sum(a.distance_m) desc, count(*) desc, e.name
            limit 1
            """, bounds, cancellationToken: ct));
        return new YearReviewResult(year, year < currentYear, Summarize(rows),
            previous.Count > 0 ? previous : null, records.ToArray(), equipment);
    }

    public static void MapYearReview(this RouteGroupBuilder api)
    {
        api.MapGet("/activities/year-review", async (int year, string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (year is < 2 or > 9998)
                return Results.BadRequest(new ApiError("Ange ett år mellan 1 och 9998.", "invalidActivityYear"));
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await ReadAsync(conn, user.GetUserId(), year, timeZone, DateTime.UtcNow, ct));
        }).RequireAuthorization();
    }
}
