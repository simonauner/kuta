using System.Security.Claims;
using Dapper;
using Npgsql;

// Mål i SI-enheter: distance i meter, duration i sekunder, count i antal pass.
// SportGroup är en nyckel ur SportGroups.All (t.ex. "running" med terräng och virtuell löpning); null = alla.
// Privacy null = privat vid skapande och oförändrad vid redigering.
public record GoalRequest(string? SportGroup, string? Metric, string? Period, double? Target, bool Recurring = false,
    string? TimeZone = null, string? Privacy = null);
public record StoredGoal(Guid Id, string? SportGroup, string Metric, string Period, double Target, bool Recurring, DateOnly StartsOn,
    string Privacy = "private");
public record GoalPeriodProgress(DateOnly Start, DateOnly End, double Value, bool Achieved);
public record GoalProgress(Guid Id, string? SportGroup, string Metric, string Period, double Target, bool Recurring,
    DateOnly StartsOn, string Privacy, bool Ended, GoalPeriodProgress Current, GoalPeriodProgress? Previous, int Streak);

public static class Goals
{
    public static readonly string[] Metrics = ["distance", "duration", "count"];
    public static readonly string[] Periods = ["week", "month", "year"];
    static readonly ApiError InvalidGoal = new("Kontrollera sport, typ, period och mål.", "invalidGoal");

    public static ApiError? Validate(GoalRequest req) =>
        (req.SportGroup is not null && !SportGroups.IsValid(req.SportGroup))
        || (req.Privacy is not null && !ActivityInput.IsValidPrivacy(req.Privacy))
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
                goal.Privacy, ended, current, previous, streak);
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
        // En löpares mål enligt varje måls synlighet (ägaren ser alla); okänd löpare ger 404.
        api.MapGet("/runners/{id:guid}/goals", async (Guid id, string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct)) return InvalidTimeZone();
            if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from users where id = @id)", new { id }, cancellationToken: ct)))
                return Results.NotFound();
            return Results.Ok(await LoadAsync(conn, id, timeZone, DateTimeOffset.UtcNow, ct, user.GetUserId()));
        }).RequireAuthorization();
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
            insert into goals (user_id, sport_group, metric, period, target, recurring, starts_on, privacy)
            values (@userId, @SportGroup, @Metric, @Period, @Target, @Recurring, (@now at time zone @timeZone)::date,
                coalesce(@Privacy, 'private'))
            returning id
            """, new { userId, req.SportGroup, req.Metric, req.Period, req.Target, req.Recurring, now, timeZone, req.Privacy },
            cancellationToken: ct));

    // Startdatumet behålls: ett ändrat mål räknas från perioden som innehåller det ursprungliga datumet.
    public static async Task<bool> UpdateAsync(NpgsqlConnection conn, Guid userId, Guid id, GoalRequest req, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("""
            update goals set sport_group = @SportGroup, metric = @Metric, period = @Period, target = @Target,
                recurring = @Recurring, privacy = coalesce(@Privacy, privacy)
            where id = @id and user_id = @userId
            """, new { id, userId, req.SportGroup, req.Metric, req.Period, req.Target, req.Recurring, req.Privacy },
            cancellationToken: ct)) > 0;

    public static async Task<bool> DeleteAsync(NpgsqlConnection conn, Guid userId, Guid id, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("delete from goals where id = @id and user_id = @userId",
            new { id, userId }, cancellationToken: ct)) > 0;

    // viewerId null = ägaren själv (alla mål). Annars passens läsregel per mål: public för alla inloggade,
    // followers bara vid godkänd följrelation.
    public static async Task<StoredGoal[]> LoadStoredAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct,
        NpgsqlTransaction? tx = null, Guid? viewerId = null) =>
        (await conn.QueryAsync<StoredGoal>(new CommandDefinition("""
            select id, sport_group, metric, period, target, recurring, starts_on, privacy from goals g
            where user_id = @userId and (@viewerId::uuid is null or g.user_id = @viewerId or g.privacy = 'public' or
                (g.privacy = 'followers' and exists (select 1 from follows f
                    where f.follower_id = @viewerId and f.following_id = g.user_id and f.accepted_at is not null)))
            order by created_at, id
            """, new { userId, viewerId }, tx, cancellationToken: ct))).ToArray();

    public static async Task<GoalProgress[]> LoadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTimeOffset now, CancellationToken ct, Guid? viewerId = null)
    {
        var goals = await LoadStoredAsync(conn, userId, ct, viewerId: viewerId);
        if (goals.Length == 0) return [];
        var today = await conn.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "select (@now at time zone @timeZone)::date", new { now, timeZone }, cancellationToken: ct));
        // Samma effektiva värden som statistiken: redigerade delars distans, och gruppmål räknar matchande delar
        // av multisportpass. Utan grupp räknas varje pass en gång.
        var buckets = await conn.QueryAsync<GoalBucket>(new CommandDefinition($"""
            with {SportGroups.Cte}, {SportGroups.EffectiveCte}
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
