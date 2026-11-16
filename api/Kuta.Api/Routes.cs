using System.Security.Claims;
using Dapper;
using Npgsql;

public record SimilarRouteActivity(
    Guid Id, string Sport, string? Title, DateTime StartedAt, int DurationS, int? MovingS, double DistanceM, string? Attribution);

public record RouteSummary(
    Guid Id, string SportGroup, int Count, double DistanceM, int BestDurationS, Guid LatestActivityId,
    DateTime LatestStartedAt, string RouteGeoJson, string? Attribution, double RouteDistanceM);

// Grouping is true while the routes job has activities left to assign, so the items may still change.
public record RouteRead<T>(IReadOnlyList<T> Items, bool Grouping);

// The owner's routes: each activity with a GPS line belongs to one route (migration 034). An activity joins the route
// whose line it matches best, otherwise it starts a new route with its own line as the reference. A match is the same
// sport group, a length within 5 % and a Fréchet distance within the tolerance. Fréchet follows both lines in order,
// so a loop run the other way is a different route. Only the owner's activities are grouped and shown.
public static class Routes
{
    // shortcut: initial values from AGENTS.md (segments), not yet calibrated against real GPS tracks. Changing them
    // only affects new assignments; clear activities.route_id and delete the routes to regroup existing activities.
    public const double ToleranceM = 30;
    public const double LengthTolerance = 0.05;
    // Both lines are densified so the discrete Fréchet distance is close to the continuous one (it overshoots by at most
    // half a step, which the threshold allows). At most MaxPoints per line keeps one comparison to a few milliseconds.
    const double MinStepM = 20;
    const int MaxPoints = 500;

    // SQL for reference line r and candidate line c (geography). The metric projection is the UTM zone of r's start.
    static string Srid(string r) => $"""
        (case when st_y(st_startpoint({r}::geometry)) < 0 then 32700 else 32600 end
            + least(60, floor((st_x(st_startpoint({r}::geometry)) + 180) / 6)::int + 1))
        """;
    static string Step(string r) => $"greatest(@minStep, st_length({r}) / @maxPoints)";
    static string Candidate(string r, string c) => $"""
        abs(st_length({c}) - st_length({r})) <= st_length({r}) * @lengthTolerance
        and st_dwithin(st_startpoint({c}::geometry)::geography, st_startpoint({r}::geometry)::geography, @tolerance)
        and st_dwithin(st_endpoint({c}::geometry)::geography, st_endpoint({r}::geometry)::geography, @tolerance)
        """;
    static string Frechet(string r, string c) => $"""
        st_frechetdistance(st_segmentize(st_transform({c}::geometry, {Srid(r)}), {Step(r)}),
            st_segmentize(st_transform({r}::geometry, {Srid(r)}), {Step(r)}))
        """;

    static DynamicParameters Parameters(object values)
    {
        var parameters = new DynamicParameters(values);
        parameters.AddDynamicParams(new
        {
            tolerance = ToleranceM, lengthTolerance = LengthTolerance, minStep = MinStepM, maxPoints = MaxPoints,
            groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys,
        });
        return parameters;
    }

    const string Pending = "a.user_id = @userId and a.route is not null and a.route_id is null";

    // Queues the routes job when the user has activities without a route; true while any are left to assign.
    // Reads call this instead of assigning, so no request holds the profile lock for long.
    static async Task<bool> QueuePendingAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct)
    {
        if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                $"select exists (select 1 from activities a where {Pending})", new { userId }, cancellationToken: ct)))
            return false;
        await JobQueue.EnqueueAsync(conn, userId, JobKinds.Routes, ct: ct);
        return true;
    }

    public sealed record JobResult(int Assigned);

    // Assigns the user's pending activities oldest first, so the oldest activity on a course becomes its reference. Each
    // activity is its own short transaction under the profile lock: imports wait for one activity at most, concurrent
    // assignments never create the same route twice, and an interrupted job keeps what it has done.
    public static async Task<JobResult> RunJobAsync(NpgsqlConnection conn, JobRow job, CancellationToken ct)
    {
        var assigned = 0;
        while (await AssignNextAsync(conn, job.UserId, ct))
            if (++assigned % 20 == 0 && !await JobQueue.HeartbeatAsync(conn, job.Id, null, ct))
                throw new JobFailedException("jobCancelled");
        return new JobResult(assigned);
    }

    static async Task<bool> AssignNextAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("select id from users where id = @userId for update",
            new { userId }, tx, cancellationToken: ct));
        var id = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            $"select a.id from activities a where {Pending} order by a.started_at, a.id limit 1",
            new { userId }, tx, cancellationToken: ct));
        if (id is null) return false;
        await conn.ExecuteAsync(new CommandDefinition($"""
            with {SportGroups.Cte},
            a as (
                select a.route, coalesce(m.sport_group, a.sport) as sport_group
                from activities a left join sport_groups m on m.sport = a.sport
                where a.id = @id
            ),
            match as (
                select r.id
                from a join routes r on r.user_id = @userId and r.sport_group = a.sport_group
                    and st_dwithin(r.route, st_startpoint(a.route::geometry)::geography, @tolerance)
                cross join lateral (select {Frechet("r.route", "a.route")} as distance) f
                where {Candidate("r.route", "a.route")} and f.distance <= @tolerance + {Step("r.route")} / 2
                order by f.distance
                limit 1
            ),
            created as (
                insert into routes (user_id, sport_group, route, length_m)
                select @userId, a.sport_group, a.route, st_length(a.route) from a
                where not exists (select 1 from match)
                returning id
            )
            update activities set route_id = coalesce((select id from match), (select id from created))
            where id = @id
            """, Parameters(new { id, userId }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    // Activities on the same route as one of the owner's activities, newest first; null if it is not the owner's.
    public static async Task<RouteRead<SimilarRouteActivity>?> ReadSimilarAsync(
        NpgsqlConnection conn, Guid activityId, Guid userId, CancellationToken ct = default)
    {
        if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists (select 1 from activities where id = @activityId and user_id = @userId)",
                new { activityId, userId }, cancellationToken: ct)))
            return null;
        var grouping = await QueuePendingAsync(conn, userId, ct);
        return new((await conn.QueryAsync<SimilarRouteActivity>(new CommandDefinition($"""
            select a.id, a.sport, a.title, a.started_at, a.duration_s, a.moving_s, {Multisport.DistanceSql("a")} as distance_m,
                {GarminAttribution.Sql("a")} as attribution
            from activities t join activities a on a.route_id = t.route_id and a.user_id = @userId
            where t.id = @activityId and t.user_id = @userId
            order by a.started_at desc, a.id desc
            """, new { activityId, userId }, cancellationToken: ct))).AsList(), grouping);
    }

    public const int PageSize = 10;

    // The owner's most run routes (at least twice), most runs first, PageSize at a time from offset.
    // With a year, only that year's runs (local time in timeZone) count; sportGroup is a key from SportGroups.All.
    public static async Task<RouteRead<RouteSummary>> ReadTopAsync(NpgsqlConnection conn, Guid userId, int offset = 0,
        int? year = null, string timeZone = "UTC", string? sportGroup = null, CancellationToken ct = default)
    {
        var grouping = await QueuePendingAsync(conn, userId, ct);
        return new((await conn.QueryAsync<RouteSummary>(new CommandDefinition($"""
            select r.id, r.sport_group, count(*)::int as count, sum({Multisport.DistanceSql("a")}) as distance_m,
                min(a.duration_s) as best_duration_s, (array_agg(a.id order by a.started_at desc))[1] as latest_activity_id,
                max(a.started_at) as latest_started_at, st_asgeojson(st_simplify(r.route::geometry, 0.00005), 5) as route_geo_json,
                -- Garmin's rule: data from Garmin is marked wherever it is shown
                case when bool_or(a.source = 'garmin') then 'Garmin' end as attribution,
                avg({Multisport.DistanceSql("a")}) as route_distance_m
            from routes r join activities a on a.route_id = r.id
            where r.user_id = @userId
                and (@sportGroup::text is null or r.sport_group = @sportGroup)
                and (@year::int is null or (a.started_at >= (make_date(@year, 1, 1)::timestamp at time zone @timeZone)
                    and a.started_at < (make_date(@year + 1, 1, 1)::timestamp at time zone @timeZone)))
            group by r.id
            having count(*) >= 2
            order by count(*) desc, max(a.started_at) desc, r.id
            limit @PageSize offset @offset
            """, new { userId, PageSize, offset, year, timeZone, sportGroup }, cancellationToken: ct))).AsList(), grouping);
    }

    public static void MapRoutes(this RouteGroupBuilder api)
    {
        api.MapGet("/activities/{id:guid}/similar", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await ReadSimilarAsync(conn, id, user.GetUserId(), ct) is { } read ? Results.Ok(read) : Results.NotFound();
        }).RequireAuthorization();

        api.MapGet("/routes", async (int? offset, int? year, string? timeZone, string? sportGroup,
            NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (year is < 1 or > 9998)
                return Results.BadRequest(new ApiError("Ange ett år mellan 1 och 9998.", "invalidActivityYear"));
            if (sportGroup is not null && !SportGroups.IsValid(sportGroup))
                return Results.BadRequest(new ApiError("Välj en giltig sport.", "invalidSportGroup"));
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await ReadTopAsync(conn, user.GetUserId(), Math.Max(0, offset ?? 0), year, timeZone, sportGroup, ct));
        }).RequireAuthorization();
    }
}
