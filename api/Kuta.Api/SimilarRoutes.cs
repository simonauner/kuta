using System.Security.Claims;
using Dapper;
using Npgsql;

public record SimilarRouteActivity(
    Guid Id, string Sport, string? Title, DateTime StartedAt, int DurationS, int? MovingS, double DistanceM, string? Attribution);

// Own activities on the same route as one activity: same sport group, route length within 5 % and a Fréchet distance
// within the tolerance. Fréchet follows both lines in order, so a loop run the other way is a different route, and it
// bounds the start and finish distances too. Computed on read; only the owner's activities are compared.
public static class SimilarRoutes
{
    // shortcut: initial values from AGENTS.md (segments), not yet calibrated against real GPS tracks; tune them there.
    public const double ToleranceM = 30;
    public const double LengthTolerance = 0.05;
    // Both lines are densified so the discrete Fréchet distance is close to the continuous one (it overshoots by at most
    // half a step, which the threshold allows). At most MaxPoints per line keeps one comparison to a few milliseconds.
    const double MinStepM = 20;
    const int MaxPoints = 500;

    public static async Task<IReadOnlyList<SimilarRouteActivity>?> ReadAsync(
        NpgsqlConnection conn, Guid activityId, Guid userId, CancellationToken ct = default)
    {
        if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists (select 1 from activities where id = @activityId and user_id = @userId)",
                new { activityId, userId }, cancellationToken: ct)))
            return null;
        // The UTM zone of the target's start gives a metric projection for both lines.
        return (await conn.QueryAsync<SimilarRouteActivity>(new CommandDefinition($"""
            with {SportGroups.Cte},
            target as (
                select t.route, st_length(t.route) as length_m, coalesce(m.sport_group, t.sport) as sport_group,
                    st_startpoint(t.route::geometry) as start, st_endpoint(t.route::geometry) as finish,
                    greatest(@minStep, st_length(t.route) / @maxPoints) as step
                from activities t left join sport_groups m on m.sport = t.sport
                where t.id = @activityId and t.user_id = @userId and t.route is not null
            ),
            projected as (
                select target.*, st_segmentize(st_transform(target.route::geometry, zone.srid), target.step) as line, zone.srid
                from target cross join lateral (
                    select (case when st_y(start) < 0 then 32700 else 32600 end
                        + least(60, floor((st_x(start) + 180) / 6)::int + 1)) as srid
                ) zone
            )
            select a.id, a.sport, a.title, a.started_at, a.duration_s, a.moving_s, a.distance_m,
                {GarminAttribution.Sql("a")} as attribution
            from projected p
            join activities a on a.user_id = @userId and a.route is not null
                and st_dwithin(a.route, p.start::geography, @tolerance)
            left join sport_groups m on m.sport = a.sport
            where coalesce(m.sport_group, a.sport) = p.sport_group
              and abs(st_length(a.route) - p.length_m) <= p.length_m * @lengthTolerance
              and st_dwithin(st_startpoint(a.route::geometry)::geography, p.start::geography, @tolerance)
              and st_dwithin(st_endpoint(a.route::geometry)::geography, p.finish::geography, @tolerance)
              and st_frechetdistance(st_segmentize(st_transform(a.route::geometry, p.srid), p.step), p.line)
                  <= @tolerance + p.step / 2
            order by a.started_at desc, a.id desc
            """, new
            {
                activityId, userId, tolerance = ToleranceM, lengthTolerance = LengthTolerance, minStep = MinStepM, maxPoints = MaxPoints,
                groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys,
            }, cancellationToken: ct))).ToList();
    }

    public static void MapSimilarRoutes(this RouteGroupBuilder api) =>
        api.MapGet("/activities/{id:guid}/similar", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await ReadAsync(conn, id, user.GetUserId(), ct) is { } items ? Results.Ok(items) : Results.NotFound();
        }).RequireAuthorization();
}
