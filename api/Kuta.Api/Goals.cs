using System.Security.Claims;
using Dapper;
using Npgsql;

// Mål i SI-enheter: distance i meter, duration i sekunder, count i antal pass. Sport null = alla sporter.
public record GoalRequest(string? Sport, string? Metric, string? Period, double? Target, bool Recurring = false,
    string? TimeZone = null);
public record StoredGoal(Guid Id, string? Sport, string Metric, string Period, double Target, bool Recurring, DateOnly StartsOn);
public record GoalPeriodProgress(DateOnly Start, DateOnly End, double Value, bool Achieved);
public record GoalProgress(Guid Id, string? Sport, string Metric, string Period, double Target, bool Recurring,
    DateOnly StartsOn, bool Ended, GoalPeriodProgress Current, GoalPeriodProgress? Previous, int Streak);

public static class Goals
{
    public static readonly string[] Metrics = ["distance", "duration", "count"];
    public static readonly string[] Periods = ["week", "month", "year"];
    static readonly ApiError InvalidGoal = new("Kontrollera sport, typ, period och mål.", "invalidGoal");

    public static ApiError? Validate(GoalRequest req) =>
        (req.Sport is not null && !ActivityInput.Sports.Contains(req.Sport))
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

    // values: summa per periodstart (lokalt datum); saknad period = 0. Ett engångsmål gäller perioden som innehåller
    // startdatumet. Upprepade mål räknar även tidigare pass, så att förra perioden och raden syns direkt.
    public static GoalProgress Evaluate(StoredGoal goal, DateOnly today, IReadOnlyDictionary<DateOnly, double> values)
    {
        GoalPeriodProgress Make(DateOnly start)
        {
            var value = values.GetValueOrDefault(start);
            return new(start, NextPeriod(goal.Period, start), value, value >= goal.Target);
        }

        var currentStart = PeriodStart(goal.Period, today);
        if (!goal.Recurring)
        {
            var first = PeriodStart(goal.Period, goal.StartsOn);
            return Result(Make(first), currentStart > first, null, 0);
        }

        var current = Make(currentStart);
        var previous = Make(PreviousPeriod(goal.Period, currentStart));
        // Raden slutar vid första missade perioden; perioder utan pass är alltid missade eftersom målet är > 0.
        var streak = 0;
        for (var period = previous; period.Achieved; period = Make(PreviousPeriod(goal.Period, period.Start)))
            streak++;
        // Pågående period räknas in först när den är uppnådd.
        return Result(current, false, previous, streak + (current.Achieved ? 1 : 0));

        GoalProgress Result(GoalPeriodProgress current, bool ended, GoalPeriodProgress? previous, int streak) =>
            new(goal.Id, goal.Sport, goal.Metric, goal.Period, goal.Target, goal.Recurring, goal.StartsOn,
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
            insert into goals (user_id, sport, metric, period, target, recurring, starts_on)
            values (@userId, @Sport, @Metric, @Period, @Target, @Recurring, (@now at time zone @timeZone)::date)
            returning id
            """, new { userId, req.Sport, req.Metric, req.Period, req.Target, req.Recurring, now, timeZone },
            cancellationToken: ct));

    // Startdatumet behålls: ett ändrat engångsmål gäller perioden som innehåller det ursprungliga datumet.
    public static async Task<bool> UpdateAsync(NpgsqlConnection conn, Guid userId, Guid id, GoalRequest req, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("""
            update goals set sport = @Sport, metric = @Metric, period = @Period, target = @Target, recurring = @Recurring
            where id = @id and user_id = @userId
            """, new { id, userId, req.Sport, req.Metric, req.Period, req.Target, req.Recurring }, cancellationToken: ct)) > 0;

    public static async Task<bool> DeleteAsync(NpgsqlConnection conn, Guid userId, Guid id, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("delete from goals where id = @id and user_id = @userId",
            new { id, userId }, cancellationToken: ct)) > 0;

    public static async Task<StoredGoal[]> LoadStoredAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct,
        NpgsqlTransaction? tx = null) =>
        (await conn.QueryAsync<StoredGoal>(new CommandDefinition("""
            select id, sport, metric, period, target, recurring, starts_on from goals
            where user_id = @userId order by created_at, id
            """, new { userId }, tx, cancellationToken: ct))).ToArray();

    public static async Task<GoalProgress[]> LoadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTimeOffset now, CancellationToken ct)
    {
        var goals = await LoadStoredAsync(conn, userId, ct);
        if (goals.Length == 0) return [];
        var today = await conn.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "select (@now at time zone @timeZone)::date", new { now, timeZone }, cancellationToken: ct));
        // Samma effektiva värden som statistiken: redigerade delars distans, och sportspecifika mål räknar
        // matchande delar av multisportpass. Utan sport räknas varje pass en gång.
        var buckets = await conn.QueryAsync<GoalBucket>(new CommandDefinition("""
            with effective as (
                select a.id, a.started_at, a.sport, a.duration_s::double precision as duration_s,
                    coalesce((select sum(p.distance_m) from activity_parts p
                        where p.activity_id = a.id and p.original = not a.parts_edited), a.distance_m) as distance_m,
                    false as part
                from activities a where a.user_id = @userId
                union all
                select a.id, a.started_at, p.sport, sum(p.end_s - p.start_s)::double precision, sum(p.distance_m), true
                from activities a join activity_parts p on p.activity_id = a.id and p.original = not a.parts_edited
                where a.user_id = @userId and p.sport <> a.sport
                group by a.id, a.started_at, p.sport
            )
            select g.id as goal_id, b.period_start, b.value
            from goals g
            cross join lateral (
                select date_trunc(g.period, e.started_at at time zone @timeZone)::date as period_start,
                    (case g.metric when 'distance' then sum(e.distance_m)
                        when 'duration' then sum(e.duration_s)
                        else count(distinct e.id) end)::double precision as value
                from effective e
                where (case when g.sport is null then not e.part else e.sport = g.sport end)
                    and (g.recurring or e.started_at >= (date_trunc(g.period, g.starts_on::timestamp) at time zone @timeZone))
                group by 1
            ) b
            where g.user_id = @userId
            """, new { userId, timeZone }, cancellationToken: ct));
        var byGoal = buckets.ToLookup(b => b.GoalId);
        return goals.Select(goal => Evaluate(goal, today,
            byGoal[goal.Id].ToDictionary(b => b.PeriodStart, b => b.Value))).ToArray();
    }

    record GoalBucket(Guid GoalId, DateOnly PeriodStart, double Value);
}
