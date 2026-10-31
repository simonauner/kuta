using System.Security.Claims;
using Dapper;
using Npgsql;

public record Runner(Guid Id, string DisplayName, string Status);
public record FollowLists(IEnumerable<Runner> Following, IEnumerable<Runner> Followers);
public record FeedItem(Guid Id, Guid UserId, string DisplayName, string Sport, string? Title,
    DateTime StartedAt, int DurationS, double DistanceM, string Privacy, string? RouteGeoJson);
public record FeedPage(IReadOnlyList<FeedItem> Items, ActivityCursor? NextCursor);

public static class SocialEndpoints
{
    // The same predicate protects detail, best efforts and the feed. Writes remain owner-only.
    public const string CanRead = """
        (a.user_id = @userId or a.privacy = 'public' or
         (a.privacy = 'followers' and exists (select 1 from follows f
          where f.follower_id = @userId and f.following_id = a.user_id and f.accepted_at is not null)))
        """;

    public static void MapSocial(this RouteGroupBuilder api)
    {
        var social = api.MapGroup("").RequireAuthorization();
        social.MapGet("/runners/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var runner = await conn.QuerySingleOrDefaultAsync<Runner>("""
                select u.id, u.display_name, case when f.follower_id is null then 'none'
                    when f.accepted_at is null then 'pending' else 'accepted' end as status
                from users u left join follows f on f.following_id = u.id and f.follower_id = @userId
                where u.id = @id
                """, new { id, userId = user.GetUserId() });
            return runner is null ? Results.NotFound() : Results.Ok(runner);
        });
        social.MapGet("/runners/{id:guid}/activities", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user,
            DateTimeOffset? beforeStartedAt, Guid? beforeId) =>
        {
            if (beforeStartedAt.HasValue != beforeId.HasValue)
                return Results.BadRequest(new ApiError("Sidmarkören är ogiltig. Ladda om sidan.", "invalidActivityPage"));
            await using var conn = await db.OpenConnectionAsync();
            if (!await conn.ExecuteScalarAsync<bool>("select exists(select 1 from users where id = @id)", new { id }))
                return Results.NotFound();
            return Results.Ok(await ReadFeedAsync(conn, user.GetUserId(), beforeStartedAt, beforeId, id));
        });
        social.MapGet("/follows", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            var following = await conn.QueryAsync<Runner>("""
                select u.id, u.display_name, case when f.accepted_at is null then 'pending' else 'accepted' end as status
                from follows f join users u on u.id = f.following_id where f.follower_id = @userId
                order by u.display_name, u.id
                """, new { userId });
            var followers = await conn.QueryAsync<Runner>("""
                select u.id, u.display_name, case when f.accepted_at is null then 'pending' else 'accepted' end as status
                from follows f join users u on u.id = f.follower_id where f.following_id = @userId
                order by f.accepted_at nulls first, u.display_name, u.id
                """, new { userId });
            return Results.Ok(new FollowLists(following, followers));
        });
        social.MapPost("/following/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            var target = await conn.ExecuteScalarAsync<Guid?>("""
                insert into follows (follower_id, following_id)
                select @userId, id from users where id = @id and id <> @userId
                    and exists (select 1 from users where id = @userId)
                on conflict (follower_id, following_id) do update set created_at = follows.created_at
                returning following_id
                """, new { id, userId });
            return target is null ? Results.NotFound() : Results.NoContent();
        });
        social.MapPut("/followers/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var count = await conn.ExecuteAsync("""
                update follows set accepted_at = coalesce(accepted_at, now())
                where following_id = @userId and follower_id = @id
                """, new { id, userId = user.GetUserId() });
            return count == 0 ? Results.NotFound() : Results.NoContent();
        });
        social.MapDelete("/following/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from follows where follower_id = @userId and following_id = @id",
                new { id, userId = user.GetUserId() });
            return Results.NoContent();
        });
        social.MapDelete("/followers/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from follows where following_id = @userId and follower_id = @id",
                new { id, userId = user.GetUserId() });
            return Results.NoContent();
        });
        social.MapGet("/feed", async (NpgsqlDataSource db, ClaimsPrincipal user,
            DateTimeOffset? beforeStartedAt, Guid? beforeId) =>
        {
            if (beforeStartedAt.HasValue != beforeId.HasValue)
                return Results.BadRequest(new ApiError("Sidmarkören är ogiltig. Ladda om sidan.", "invalidActivityPage"));
            await using var conn = await db.OpenConnectionAsync();
            return Results.Ok(await ReadFeedAsync(conn, user.GetUserId(), beforeStartedAt, beforeId));
        });
    }

    public static async Task<FeedPage> ReadFeedAsync(NpgsqlConnection conn, Guid userId,
        DateTimeOffset? beforeStartedAt = null, Guid? beforeId = null, Guid? runnerId = null)
    {
        // Limit before route processing. The 510 m buffers allow polygon/rounding error while hiding at least 500 m.
        // Simplify before clipping so simplification cannot reconnect a line through a protected zone.
        var rows = (await conn.QueryAsync<FeedItem>($"""
            with page as materialized (
                select a.id, a.user_id, u.display_name, a.sport, a.title, a.started_at, a.duration_s,
                    coalesce((select sum(p.distance_m) from activity_parts p where p.activity_id = a.id
                        and p.original = not a.parts_edited), a.distance_m) as distance_m, a.privacy, a.route
                from activities a join users u on u.id = a.user_id
                where {CanRead} and (
                    (@runnerId::uuid is not null and a.user_id = @runnerId) or
                    (@runnerId::uuid is null and (a.user_id = @userId or exists (select 1 from follows f
                        where f.follower_id = @userId and f.following_id = a.user_id and f.accepted_at is not null))))
                  and (@beforeStartedAt::timestamptz is null or (a.started_at, a.id) < (@beforeStartedAt, @beforeId))
                order by a.started_at desc, a.id desc limit 26
            )
            select id, user_id, display_name, sport, title, started_at, duration_s, distance_m, privacy,
                case when st_isempty(preview.route) then null else st_asgeojson(preview.route, 6) end as route_geo_json
            from page
            cross join lateral (
                select st_unaryunion(st_collect(zone)) as areas from (
                    select st_buffer(st_startpoint(route::geometry)::geography, 510)::geometry as zone
                    union all
                    select st_buffer(st_endpoint(route::geometry)::geography, 510)::geometry
                    union all
                    select st_buffer(st_setsrid(st_makepoint(longitude, latitude), 4326)::geography, radius_m + 10)::geometry
                    from privacy_places where user_id = page.user_id
                ) hidden where page.user_id <> @userId
            ) zones
            cross join lateral (
                select case when user_id = @userId then st_simplifypreservetopology(route::geometry, 0.0001)
                else st_collectionextract(st_difference(
                    st_simplifypreservetopology(route::geometry, 0.0001),
                    zones.areas), 2) end as route
            ) preview
            order by started_at desc, id desc
            """, new { userId, beforeStartedAt, beforeId, runnerId })).ToList();
        var items = rows.Take(25).ToList();
        return new FeedPage(items, rows.Count > 25 ? new ActivityCursor(items[^1].StartedAt, items[^1].Id) : null);
    }
}
