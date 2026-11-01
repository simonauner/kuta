using System.Security.Claims;
using Dapper;
using Npgsql;

// JoinedAt is shown only for someone asking to follow: names are free, so account age helps against impersonation.
public record Runner(Guid Id, string DisplayName, string Status, string? SupporterTier, DateTime? JoinedAt);
public record FollowLists(IEnumerable<Runner> Following, IEnumerable<Runner> Followers, IEnumerable<Runner> Blocked);
public record FeedItem(Guid Id, Guid UserId, string DisplayName, string Sport, string? Title,
    DateTime StartedAt, int DurationS, double DistanceM, string Privacy, string? RouteGeoJson, string? SupporterTier,
    string? Attribution);
public record FeedPage(IReadOnlyList<FeedItem> Items, ActivityCursor? NextCursor);

public static class SocialEndpoints
{
    // The same predicate protects detail, best efforts and the feed. Writes remain owner-only.
    public const string CanRead = """
        (a.user_id = @userId or a.privacy = 'public' or
         (a.privacy = 'followers' and exists (select 1 from follows f
          where f.follower_id = @userId and f.following_id = a.user_id and f.accepted_at is not null)))
        """;

    // Visitors without an account, via a shared link: only runners who turned on public_profile, and only activities
    // that are public. Guid.Empty is no user, so CanRead allows exactly the public activities and every route is masked.
    public static void MapPublic(this RouteGroupBuilder api)
    {
        var open = api.MapGroup("/public").RequireRateLimiting("public");
        open.MapGet("/runners/{id:guid}", async (Guid id, NpgsqlDataSource db) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var runner = await conn.QuerySingleOrDefaultAsync<Runner>($"""
                select u.id, u.display_name, 'none' as status, {Supporters.VisibleTier} as supporter_tier,
                    null::timestamptz as joined_at
                from users u where u.id = @id and u.public_profile
                """, new { id, userId = Guid.Empty });
            return runner is null ? Results.NotFound() : Results.Ok(runner);
        });
        open.MapGet("/runners/{id:guid}/activities", async (Guid id, NpgsqlDataSource db,
            DateTimeOffset? beforeStartedAt, Guid? beforeId) =>
        {
            if (beforeStartedAt.HasValue != beforeId.HasValue)
                return Results.BadRequest(new ApiError("Sidmarkören är ogiltig. Ladda om sidan.", "invalidActivityPage"));
            await using var conn = await db.OpenConnectionAsync();
            if (!await IsPublicAsync(conn, id)) return Results.NotFound();
            return Results.Ok(await ReadFeedAsync(conn, Guid.Empty, beforeStartedAt, beforeId, id));
        });
        open.MapGet("/activities/{id:guid}", async (Guid id, NpgsqlDataSource db, CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var detail = await ActivityEndpoints.ReadDetailAsync(conn, Guid.Empty, id, cancellationToken);
            return detail is not null && await IsPublicAsync(conn, detail.UserId) ? Results.Ok(detail) : Results.NotFound();
        });

        static Task<bool> IsPublicAsync(NpgsqlConnection conn, Guid userId) =>
            conn.ExecuteScalarAsync<bool>("select exists(select 1 from users where id = @userId and public_profile)", new { userId });
    }

    public static void MapSocial(this RouteGroupBuilder api)
    {
        var social = api.MapGroup("").RequireAuthorization();
        social.MapGet("/runners/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var runner = await conn.QuerySingleOrDefaultAsync<Runner>($"""
                select u.id, u.display_name, case when f.follower_id is null then 'none'
                    when f.accepted_at is null then 'pending' else 'accepted' end as status,
                    {Supporters.VisibleTier} as supporter_tier, null::timestamptz as joined_at
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
            var following = await conn.QueryAsync<Runner>($"""
                select u.id, u.display_name, case when f.accepted_at is null then 'pending' else 'accepted' end as status,
                    {Supporters.VisibleTier} as supporter_tier, null::timestamptz as joined_at
                from follows f join users u on u.id = f.following_id where f.follower_id = @userId
                order by u.display_name, u.id
                """, new { userId });
            var followers = await conn.QueryAsync<Runner>($"""
                select u.id, u.display_name, case when f.accepted_at is null then 'pending' else 'accepted' end as status,
                    {Supporters.VisibleTier} as supporter_tier,
                    case when f.accepted_at is null then u.created_at end as joined_at
                from follows f join users u on u.id = f.follower_id where f.following_id = @userId
                order by f.accepted_at nulls first, u.display_name, u.id
                """, new { userId });
            var blocked = await conn.QueryAsync<Runner>("""
                select u.id, u.display_name, 'blocked' as status, null as supporter_tier, null::timestamptz as joined_at
                from follow_blocks b join users u on u.id = b.blocked_id where b.blocker_id = @userId
                order by u.display_name, u.id
                """, new { userId });
            return Results.Ok(new FollowLists(following, followers, blocked));
        });
        social.MapPost("/following/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            if (id == userId || !await conn.ExecuteScalarAsync<bool>(
                    "select exists(select 1 from users where id = @id) and exists(select 1 from users where id = @userId)", new { id, userId }))
                return Results.NotFound();
            // A blocked request is not stored, but the response is the same so the block is not revealed.
            await conn.ExecuteAsync("""
                insert into follows (follower_id, following_id)
                select @userId, @id
                where not exists (select 1 from follow_blocks where blocker_id = @id and blocked_id = @userId)
                on conflict (follower_id, following_id) do nothing
                """, new { id, userId });
            return Results.NoContent();
        }).RequireRateLimiting("follows");
        // Blocking removes the person's follow or request and stops new requests.
        social.MapPut("/blocks/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            await conn.ExecuteAsync("""
                delete from follows where following_id = @userId and follower_id = @id;
                insert into follow_blocks (blocker_id, blocked_id)
                select @userId, id from users where id = @id and id <> @userId
                on conflict do nothing
                """, new { id, userId });
            return await conn.ExecuteScalarAsync<bool>("select exists(select 1 from follow_blocks where blocker_id = @userId and blocked_id = @id)",
                new { id, userId }) ? Results.NoContent() : Results.NotFound();
        });
        social.MapDelete("/blocks/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from follow_blocks where blocker_id = @userId and blocked_id = @id",
                new { id, userId = user.GetUserId() });
            return Results.NoContent();
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

    // Moves a point up to maxOffset metres in a direction derived from the owner's secret seed and key, uniformly over the
    // disc. The same key always gives the same offset, so averaging many activities from the same place does not reveal it.
    static string ZoneShift(string point, string key, string maxOffset) => $"""
        st_project({point}, {maxOffset} * sqrt({SeedFraction(key, 1)}), 2 * pi() * {SeedFraction(key, 9)})
        """;

    // The clip points lie on a circle of known radius, so its centre (the shifted point) can be fitted from a few previews.
    // The offset therefore spans 500 m, and it is keyed per ~1 km grid cell: one start at a known place (a race) must not
    // reveal the offset used at home.
    // shortcut: a home on a cell edge gets two independent offsets, which narrows it down; key on privacy places if needed.
    static string EndpointZone(string point) => $"""
        st_buffer({ZoneShift($"{point}::geography",
            $"round(st_y({point})::numeric, 2) || ':' || round(st_x({point})::numeric, 2)", "500")}, 1010)::geometry
        """;

    static string SeedFraction(string key, int start) =>
        $"(('x' || substr(md5(page.privacy_seed::text || ':' || {key}), {start}, 8))::bit(32)::bigint / 4294967296.0)";

    public static async Task<FeedPage> ReadFeedAsync(NpgsqlConnection conn, Guid userId,
        DateTimeOffset? beforeStartedAt = null, Guid? beforeId = null, Guid? runnerId = null)
    {
        // Limit before route processing. Every zone centre is moved by a secret, stable offset (ZoneShift) and the radius
        // grows by the largest offset, so the true point stays at least 500 m (or the place's radius) inside the zone and
        // can only be located to within the offset (500 m, or the place's radius). The extra 10 m allow polygon/rounding error.
        // Simplify before clipping so simplification cannot reconnect a line through a protected zone.
        var rows = (await conn.QueryAsync<FeedItem>($"""
            with page as materialized (
                select a.id, a.user_id, u.display_name, a.sport, a.title, a.started_at, a.duration_s,
                    coalesce((select sum(p.distance_m) from activity_parts p where p.activity_id = a.id
                        and p.original = not a.parts_edited), a.distance_m) as distance_m, a.privacy, a.route, u.privacy_seed,
                    {Supporters.VisibleTier} as supporter_tier, {GarminAttribution.Sql("a")} as attribution
                from activities a join users u on u.id = a.user_id
                where {CanRead} and (
                    (@runnerId::uuid is not null and a.user_id = @runnerId) or
                    (@runnerId::uuid is null and (a.user_id = @userId or exists (select 1 from follows f
                        where f.follower_id = @userId and f.following_id = a.user_id and f.accepted_at is not null))))
                  and (@beforeStartedAt::timestamptz is null or (a.started_at, a.id) < (@beforeStartedAt, @beforeId))
                order by a.started_at desc, a.id desc limit 26
            )
            select id, user_id, display_name, sport, title, started_at, duration_s, distance_m, privacy,
                case when st_isempty(preview.route) then null else st_asgeojson(preview.route, 4) end as route_geo_json,
                supporter_tier, attribution
            from page
            cross join lateral (
                select st_unaryunion(st_collect(zone)) as areas from (
                    select {EndpointZone("st_startpoint(route::geometry)")} as zone
                    union all
                    select {EndpointZone("st_endpoint(route::geometry)")}
                    union all
                    select st_buffer({ZoneShift("st_setsrid(st_makepoint(longitude, latitude), 4326)::geography", "privacy_places.id::text", "radius_m")},
                        2 * radius_m + 10)::geometry
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
