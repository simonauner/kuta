using System.Security.Claims;
using Dapper;
using Npgsql;

// One row per own activity (Part = false) and per other sport group in a multisport activity, as SportGroups.EffectiveCte.
public sealed record AchievementRow(Guid Id, DateTime StartedAt, DateTime LocalStart, string SportGroup,
    double DistanceM, double ElevationM, bool Part);

public sealed record FirstEffort(int DistanceM, DateTime AchievedAt, Guid ActivityId);

// Kind and threshold say what was reached (the web names it and adds the comparison); Year for yearly kinds.
public sealed record Achievement(string Key, string Kind, double Threshold, int? Year, DateTime AchievedAt,
    Guid ActivityId, bool Seen);

public sealed record AchievementsSeenRequest(string[]? Keys, string? TimeZone);

// Fixed rules over the own activities, evaluated when read: nothing is stored except which ones the user has seen,
// so deleted activities take their achievements with them and changed rules apply to the whole history.
public static class Achievements
{
    // Lifetime running distance (m); the web compares them with e.g. Stockholm–Gothenburg and the equator.
    public static readonly int[] RunDistances = [100_000, 470_000, 1_000_000, 1_572_000, 5_000_000, 10_000_000, 40_075_000];
    // Running distance in one calendar year (m)
    public static readonly int[] YearRunDistances = [500_000, 1_000_000, 2_000_000, 3_000_000];
    // Elevation gain in one calendar year, all sports (m): Kebnekaise, Mont Blanc, Mount Everest and twice Everest
    public static readonly int[] Climbs = [2_097, 4_806, 8_849, 17_698];
    public static readonly int[] ActivityCounts = [10, 50, 100, 250, 500, 1000];
    // Running distance in one calendar month (m), first time only
    public static readonly int[] MonthRunDistances = [100_000, 200_000, 300_000];
    // Weeks in a row (Monday–Sunday) with at least one activity, first time only
    public static readonly int[] WeekStreaks = [4, 12, 26, 52];
    public static readonly int[] FirstDistances = [5000, 10000, 21097, 42195];
    // Longer than a marathon with margin for GPS: an ultra
    public const double UltraM = 45_000;
    public const int EarlyBirdBeforeHour = 6;
    public const int NightOwlFromHour = 22;

    const string Running = "running";

    public static IReadOnlyList<Achievement> Evaluate(IReadOnlyList<AchievementRow> rows, IReadOnlyList<FirstEffort> efforts)
    {
        var earned = new List<Achievement>();
        void Add(string key, string kind, double threshold, int? year, DateTime at, Guid activity) =>
            earned.Add(new Achievement(key, kind, threshold, year, at, activity, false));

        foreach (var effort in efforts.Where(e => FirstDistances.Contains(e.DistanceM)))
            Add($"first-{effort.DistanceM}", "firstDistance", effort.DistanceM, null, effort.AchievedAt, effort.ActivityId);

        var ordered = rows.OrderBy(r => r.StartedAt).ThenBy(r => r.Id).ThenBy(r => r.Part).ToList();
        var runs = ordered.Where(r => r.SportGroup == Running).ToList();
        if (runs.FirstOrDefault(r => r.DistanceM >= UltraM) is { } ultra)
            Add("first-ultra", "firstUltra", UltraM, null, ultra.StartedAt, ultra.Id);

        // Running totals: each threshold is reached by the activity that crosses it
        double total = 0;
        var yearTotals = new Dictionary<int, double>();
        var monthTotals = new Dictionary<(int, int), double>();
        var monthsReached = new HashSet<int>();
        foreach (var run in runs)
        {
            var before = total;
            total += run.DistanceM;
            foreach (var m in RunDistances.Where(m => before < m && total >= m))
                Add($"run-distance-{m}", "runDistance", m, null, run.StartedAt, run.Id);

            var year = run.LocalStart.Year;
            var yearBefore = yearTotals.GetValueOrDefault(year);
            yearTotals[year] = yearBefore + run.DistanceM;
            foreach (var m in YearRunDistances.Where(m => yearBefore < m && yearTotals[year] >= m))
                Add($"year-run-{year}-{m}", "yearRunDistance", m, year, run.StartedAt, run.Id);

            var month = (year, run.LocalStart.Month);
            var monthBefore = monthTotals.GetValueOrDefault(month);
            monthTotals[month] = monthBefore + run.DistanceM;
            foreach (var m in MonthRunDistances.Where(m => monthBefore < m && monthTotals[month] >= m && monthsReached.Add(m)))
                Add($"month-run-{m}", "monthRunDistance", m, null, run.StartedAt, run.Id);
        }

        // Whole activities: elevation per year, counts, streaks and time of day
        var activities = ordered.Where(r => !r.Part).ToList();
        var climbed = new Dictionary<int, double>();
        var count = 0;
        AchievementRow? earlyBird = null, nightOwl = null;
        DateOnly? week = null;
        var streak = 0;
        var streaksReached = new HashSet<int>();
        foreach (var activity in activities)
        {
            var year = activity.LocalStart.Year;
            var before = climbed.GetValueOrDefault(year);
            climbed[year] = before + Math.Max(0, activity.ElevationM);
            foreach (var m in Climbs.Where(m => before < m && climbed[year] >= m))
                Add($"climb-{year}-{m}", "climb", m, year, activity.StartedAt, activity.Id);

            count++;
            if (ActivityCounts.Contains(count))
                Add($"count-{count}", "activityCount", count, null, activity.StartedAt, activity.Id);

            var hour = activity.LocalStart.Hour;
            if (earlyBird is null && hour >= 3 && hour < EarlyBirdBeforeHour) earlyBird = activity;
            if (nightOwl is null && hour >= NightOwlFromHour) nightOwl = activity;

            var day = DateOnly.FromDateTime(activity.LocalStart);
            var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
            if (week == monday) continue;
            streak = week == monday.AddDays(-7) ? streak + 1 : 1;
            week = monday;
            if (WeekStreaks.Contains(streak) && streaksReached.Add(streak))
                Add($"week-streak-{streak}", "weekStreak", streak, null, activity.StartedAt, activity.Id);
        }
        if (earlyBird is not null) Add("early-bird", "earlyBird", EarlyBirdBeforeHour, null, earlyBird.StartedAt, earlyBird.Id);
        if (nightOwl is not null) Add("night-owl", "nightOwl", NightOwlFromHour, null, nightOwl.StartedAt, nightOwl.Id);

        return earned.OrderByDescending(a => a.AchievedAt).ThenBy(a => a.Key, StringComparer.Ordinal).ToList();
    }

    public static async Task<IReadOnlyList<Achievement>> ReadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTime now, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<AchievementRow>(new CommandDefinition($"""
            with {SportGroups.Cte}, {SportGroups.EffectiveCte}
            select e.id, e.started_at, e.started_at at time zone @timeZone as local_start, e.sport_group,
                e.distance_m::double precision as distance_m, coalesce(a.elevation_gain_m, 0)::double precision as elevation_m, e.part
            from effective e join activities a on a.id = e.id
            where e.started_at <= @now
            """, new { userId, timeZone, now, groupedSports = SportGroups.GroupedSports,
                sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct))).ToList();
        var efforts = (await conn.QueryAsync<FirstEffort>(new CommandDefinition("""
            select distinct on (distance_m) distance_m, achieved_at, activity_id
            from best_efforts where user_id = @userId and achieved_at <= @now
            order by distance_m, achieved_at, activity_id
            """, new { userId, now }, cancellationToken: ct))).ToList();
        var seen = (await conn.QueryAsync<string>(new CommandDefinition(
            "select key from achievement_views where user_id = @userId", new { userId }, cancellationToken: ct))).ToHashSet();
        return Evaluate(rows, efforts).Select(a => a with { Seen = seen.Contains(a.Key) }).ToList();
    }

    public static void MapAchievements(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/achievements").RequireAuthorization();
        group.MapGet("", async (string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await ReadAsync(conn, user.GetUserId(), timeZone, DateTime.UtcNow, ct));
        });

        // Marks the shown achievements as seen. Only currently earned keys are stored, so the table can't be filled
        // with arbitrary text, and one earned after the list was loaded stays unseen.
        group.MapPost("/seen", async (AchievementsSeenRequest req, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var timeZone = req.TimeZone ?? "UTC";
            var userId = user.GetUserId();
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            var requested = (req.Keys ?? []).ToHashSet(StringComparer.Ordinal);
            var keys = (await ReadAsync(conn, userId, timeZone, DateTime.UtcNow, ct))
                .Where(a => !a.Seen && requested.Contains(a.Key)).Select(a => a.Key).ToArray();
            await conn.ExecuteAsync(new CommandDefinition("""
                insert into achievement_views (user_id, key) select @userId, unnest(@keys)
                on conflict do nothing
                """, new { userId, keys }, cancellationToken: ct));
            return Results.NoContent();
        });
    }

    public static async Task<IEnumerable<ExportAchievementView>> LoadViewsAsync(NpgsqlConnection conn, Guid userId,
        CancellationToken ct, NpgsqlTransaction? tx = null) =>
        await conn.QueryAsync<ExportAchievementView>(new CommandDefinition(
            "select key, seen_at from achievement_views where user_id = @userId order by seen_at, key",
            new { userId }, tx, cancellationToken: ct));
}

public sealed record ExportAchievementView(string Key, DateTime SeenAt);
