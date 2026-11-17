using System.Security.Claims;
using Dapper;
using Npgsql;

public sealed record CourseLeg(int DurationS, double DistanceM);
public sealed record CourseRunner(Guid ActivityId, Guid UserId, string DisplayName, Guid? AvatarId, string Sport,
    string? Title, DateTime StartedAt, int DurationS, double DistanceM, double? CourseLengthM, string? Attribution)
{
    public CourseLeg[] Legs { get; init; } = [];
}
public sealed record SameCourseRequest(Guid? ActivityId);

// "I ran the same course" (orienteering): the owner links their activity to another activity they may read
// (activities.same_course_as). Linked activities form one course; the legs (the watch's laps) are compared side by side.
// The course is only followed through activities the viewer may read, so it never reveals links via hidden activities.
public static class SameCourse
{
    const int MaxRunners = 50;

    // Null when the viewer may not read the activity; empty when it is not linked to anything readable.
    public static async Task<IReadOnlyList<CourseRunner>?> ReadAsync(NpgsqlConnection conn, Guid id, Guid userId,
        CancellationToken ct)
    {
        var runners = (await conn.QueryAsync<CourseRunner>(new CommandDefinition($"""
            with recursive course(id) as (
                select a.id from activities a where a.id = @id and {SocialEndpoints.CanRead}
                union
                -- e is a link touching a course activity, a the activity at its other end
                select a.id
                from course c
                join activities e on e.id = c.id or e.same_course_as = c.id
                join activities a on a.id = case when e.id = c.id then e.same_course_as else e.id end
                where {SocialEndpoints.CanRead}
            )
            select a.id as activity_id, a.user_id, u.display_name, u.avatar_id, a.sport, a.title, a.started_at, a.duration_s,
                   a.distance_m, a.course_length_m, {GarminAttribution.Sql("a")} as attribution
            from course c join activities a on a.id = c.id join users u on u.id = a.user_id
            order by a.duration_s, a.id
            limit @max
            """, new { id, userId, max = MaxRunners }, cancellationToken: ct))).ToList();
        if (runners.Count == 0) return null;
        if (runners.Count == 1) return [];
        var legs = (await conn.QueryAsync<(Guid ActivityId, int DurationS, double DistanceM)>(new CommandDefinition("""
            select activity_id, duration_s, distance_m from activity_laps
            where activity_id = any(@ids) order by activity_id, lap_index
            """, new { ids = runners.Select(r => r.ActivityId).ToArray() }, cancellationToken: ct)))
            .ToLookup(l => l.ActivityId, l => new CourseLeg(l.DurationS, l.DistanceM));
        return runners.Select(r => r with { Legs = legs[r.ActivityId].ToArray() }).ToList();
    }

    public static void MapSameCourse(this RouteGroupBuilder api)
    {
        api.MapGet("/activities/{id:guid}/same-course", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await ReadAsync(conn, id, user.GetUserId(), ct) is { } runners ? Results.Ok(runners) : Results.NotFound();
        }).RequireAuthorization();

        // Links (or with null unlinks) the viewer's own activity; the other activity must be readable to them.
        api.MapPut("/activities/{id:guid}/same-course", async (Guid id, SameCourseRequest req, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var updated = await conn.ExecuteAsync(new CommandDefinition($"""
                update activities set same_course_as = @target
                where id = @id and user_id = @userId and (@target::uuid is null or exists (
                    select 1 from activities a where a.id = @target and a.id <> @id and {SocialEndpoints.CanRead}))
                """, new { id, userId = user.GetUserId(), target = req.ActivityId }, cancellationToken: ct));
            return updated == 1 ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization();
    }
}
