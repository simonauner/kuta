using System.Security.Claims;
using Dapper;
using Npgsql;

// Mål i SI-enheter: distance i meter, duration i sekunder, count i antal pass.
// SportGroup är en nyckel ur SportGroups.All (t.ex. "running" med terräng och virtuell löpning); null = alla.
public record GoalRequest(string? SportGroup, string? Metric, string? Period, double? Target, bool Recurring = false,
    string? TimeZone = null);
public record StoredGoal(Guid Id, string? SportGroup, string Metric, string Period, double Target, bool Recurring, DateOnly StartsOn);
public record GoalPeriodProgress(DateOnly Start, DateOnly End, double Value, bool Achieved);
public record GoalProgress(Guid Id, string? SportGroup, string Metric, string Period, double Target, bool Recurring,
    DateOnly StartsOn, bool Ended, GoalPeriodProgress Current, GoalPeriodProgress? Previous, int Streak);

public static class Goals
{
    public static readonly string[] Metrics = ["distance", "duration", "count"];
    public static readonly string[] Periods = ["week", "month", "year"];
    static readonly ApiError InvalidGoal = new("Kontrollera sport, typ, period och mål.", "invalidGoal");

    public static ApiError? Validate(GoalRequest req) =>
        (req.SportGroup is not null && !SportGroups.IsValid(req.SportGroup))
        || req.Metric is null || !Metrics.Contains(req.Metric) || req.Period is null || !Periods.Contains(req.Period)
        || req.Target is not { } target || !double.IsFinite(target) || target <= 0
        || target > (req.Metric == "count" ? 10_000 : 100_000_000)
        || (req.Metric == "count" && target != Math.Floor(target))
            ? InvalidGoal : null;

    // Veckor börjar på måndag (ISO), som date_trunc('week') i Postgres.
    public static DateOnly PeriodStart(string period, DateOnly date) => period switch
    {
        "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        "month" => new DateOnly(date.Year, date.Month, 1),
        _ => new DateOnly(date.Year, 1, 1),
    };

    public static DateOnly NextPeriod(string period, DateOnly start) => period switch
    {
        "week" => start.AddDays(7),
        "month" => start.AddMonths(1),
        _ => start.AddYears(1),
    };

    static DateOnly PreviousPeriod(string period, DateOnly start) => period switch
    {
        "week" => start.AddDays(-7),
        "month" => start.AddMonths(-1),
        _ => start.AddYears(-1),
    };

    // values: summa per periodstart (lokalt datum); saknad period = 0. Målet gäller från början av perioden som
    // innehåller startdatumet (1 januari för ett årsmål skapat i oktober), men aldrig tidigare perioder.
    public static GoalProgress Evaluate(StoredGoal goal, DateOnly today, IReadOnlyDictionary<DateOnly, double> values)
    {
        GoalPeriodProgress Make(DateOnly start)
        {
            var value = values.GetValueOrDefault(start);
            return new(start, NextPeriod(goal.Period, start), value, value >= goal.Target);
        }

        var first = PeriodStart(goal.Period, goal.StartsOn);
        // En annan tidszon än vid skapandet kan ge ett "idag" före första perioden.
        var currentStart = PeriodStart(goal.Period, today) is var start && start > first ? start : first;
        if (!goal.Recurring)
            return Result(Make(first), currentStart > first, null, 0);

        var current = Make(currentStart);
        GoalPeriodProgress? previous = null;
        var streak = 0;
        for (var period = PreviousPeriod(goal.Period, currentStart); period >= first; period = PreviousPeriod(goal.Period, period))
        {
            var progress = Make(period);
            previous ??= progress;
            if (!progress.Achieved) break;
            streak++;
        }
        // Pågående period räknas in först när den är uppnådd.
        return Result(current, false, previous, streak + (current.Achieved ? 1 : 0));

        GoalProgress Result(GoalPeriodProgress current, bool ended, GoalPeriodProgress? previous, int streak) =>
            new(goal.Id, goal.SportGroup, goal.Metric, goal.Period, goal.Target, goal.Recurring, goal.StartsOn,
                ended, current, previous, streak);
    }

    public static void MapGoals(this RouteGroupBuilder api)
    {
        var goals = api.MapGroup("/goals").RequireAuthorization();
        goals.MapGet("", async (string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct)) return InvalidTimeZone();
            return Results.Ok(await LoadAsync(conn, user.GetUserId(), timeZone, DateTimeOffset.UtcNow, ct));
        });
        goals.MapPost("", async (GoalRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (Validate(req) is { } invalid) return Results.BadRequest(invalid);
            var timeZone = req.TimeZone ?? "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct)) return InvalidTimeZone();
            if (await MeEndpoints.LoadProfileAsync(conn, user.GetUserId()) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            var id = await CreateAsync(conn, user.GetUserId(), req, timeZone, DateTimeOffset.UtcNow, ct);
            return Results.Created($"/api/goals/{id}", new { id });
        });
        goals.MapPut("/{id:guid}", async (Guid id, GoalRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (Validate(req) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(ct);
            return await UpdateAsync(conn, user.GetUserId(), id, req, ct) ? Results.NoContent() : Results.NotFound();
        });
        goals.MapDelete("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await DeleteAsync(conn, user.GetUserId(), id, ct) ? Results.NoContent() : Results.NotFound();
        });
    }

    static IResult InvalidTimeZone() =>
        Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));

    public static async Task<Guid> CreateAsync(NpgsqlConnection conn, Guid userId, GoalRequest req, string timeZone,
        DateTimeOffset now, CancellationToken ct) =>
        await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into goals (user_id, sport_group, metric, period, target, recurring, starts_on)
            values (@userId, @SportGroup, @Metric, @Period, @Target, @Recurring, (@now at time zone @timeZone)::date)
            returning id
            """, new { userId, req.SportGroup, req.Metric, req.Period, req.Target, req.Recurring, now, timeZone },
            cancellationToken: ct));

    // Startdatumet behålls: ett ändrat mål räknas från perioden som innehåller det ursprungliga datumet.
    public static async Task<bool> UpdateAsync(NpgsqlConnection conn, Guid userId, Guid id, GoalRequest req, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("""
            update goals set sport_group = @SportGroup, metric = @Metric, period = @Period, target = @Target, recurring = @Recurring
            where id = @id and user_id = @userId
            """, new { id, userId, req.SportGroup, req.Metric, req.Period, req.Target, req.Recurring }, cancellationToken: ct)) > 0;

    public static async Task<bool> DeleteAsync(NpgsqlConnection conn, Guid userId, Guid id, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("delete from goals where id = @id and user_id = @userId",
            new { id, userId }, cancellationToken: ct)) > 0;

    public static async Task<StoredGoal[]> LoadStoredAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct,
        NpgsqlTransaction? tx = null) =>
        (await conn.QueryAsync<StoredGoal>(new CommandDefinition("""
            select id, sport_group, metric, period, target, recurring, starts_on from goals
            where user_id = @userId order by created_at, id
            """, new { userId }, tx, cancellationToken: ct))).ToArray();

    public static async Task<GoalProgress[]> LoadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTimeOffset now, CancellationToken ct)
    {
        var goals = await LoadStoredAsync(conn, userId, ct);
        if (goals.Length == 0) return [];
        var today = await conn.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "select (@now at time zone @timeZone)::date", new { now, timeZone }, cancellationToken: ct));
        // Samma effektiva värden som statistiken: redigerade delars distans, och gruppmål räknar matchande delar
        // av multisportpass. Utan grupp räknas varje pass en gång.
        var buckets = await conn.QueryAsync<GoalBucket>(new CommandDefinition($"""
            with {SportGroups.Cte}, effective as (
                select a.id, a.started_at, coalesce(m.sport_group, a.sport) as sport_group,
                    a.duration_s::double precision as duration_s,
                    coalesce((select sum(p.distance_m) from activity_parts p
                        where p.activity_id = a.id and p.original = not a.parts_edited), a.distance_m) as distance_m,
                    false as part
                from activities a left join sport_groups m on m.sport = a.sport
                where a.user_id = @userId
                union all
                select a.id, a.started_at, coalesce(pm.sport_group, p.sport),
                    sum(p.end_s - p.start_s)::double precision, sum(p.distance_m), true
                from activities a
                join activity_parts p on p.activity_id = a.id and p.original = not a.parts_edited
                left join sport_groups am on am.sport = a.sport
                left join sport_groups pm on pm.sport = p.sport
                where a.user_id = @userId and coalesce(pm.sport_group, p.sport) <> coalesce(am.sport_group, a.sport)
                group by a.id, a.started_at, coalesce(pm.sport_group, p.sport)
            )
            select g.id as goal_id, b.period_start, b.value
            from goals g
            cross join lateral (
                select date_trunc(g.period, e.started_at at time zone @timeZone)::date as period_start,
                    (case g.metric when 'distance' then sum(e.distance_m)
                        when 'duration' then sum(e.duration_s)
                        else count(distinct e.id) end)::double precision as value
                from effective e
                where (case when g.sport_group is null then not e.part else e.sport_group = g.sport_group end)
                    and e.started_at >= (date_trunc(g.period, g.starts_on::timestamp) at time zone @timeZone)
                group by 1
            ) b
            where g.user_id = @userId
            """, new { userId, timeZone, groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct));
        var byGoal = buckets.ToLookup(b => b.GoalId);
        return goals.Select(goal => Evaluate(goal, today,
            byGoal[goal.Id].ToDictionary(b => b.PeriodStart, b => b.Value))).ToArray();
    }

    record GoalBucket(Guid GoalId, DateOnly PeriodStart, double Value);
}
