using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class SocialTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Read_rule_checks_visibility_direction_and_approval_in_postgres()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@owner), (@viewer);
            insert into users (id, display_name) values (@owner, 'Owner'), (@viewer, 'Viewer');
            insert into activities (id, user_id, source, started_at, duration_s, distance_m)
            values (@id, @owner, 'manual', now(), 1800, 5000)
            """, new { owner, viewer, id }, tx);
        async Task<bool> Read(Guid userId) => await conn.ExecuteScalarAsync<bool>(
            $"select exists(select 1 from activities a where a.id = @id and {SocialEndpoints.CanRead})", new { id, userId }, tx);
        foreach (var relation in new[] { "none", "pending", "accepted", "reverse" })
        {
            await conn.ExecuteAsync("delete from follows where follower_id in (@owner, @viewer)", new { owner, viewer }, tx);
            if (relation != "none")
                await conn.ExecuteAsync("""
                    insert into follows (follower_id, following_id, accepted_at)
                    values (@follower, @following, @accepted)
                    """, new { follower = relation == "reverse" ? owner : viewer,
                    following = relation == "reverse" ? viewer : owner,
                    accepted = relation == "pending" ? (DateTime?)null : DateTime.UtcNow }, tx);
            foreach (var privacy in new[] { "private", "followers", "public" })
            {
                await conn.ExecuteAsync("update activities set privacy = @privacy where id = @id", new { privacy, id }, tx);
                Assert.True(await Read(owner));
                Assert.Equal(privacy == "public" || privacy == "followers" && relation == "accepted", await Read(viewer));
                var profile = await SocialEndpoints.ReadFeedAsync(conn, viewer, runnerId: owner);
                Assert.Equal(privacy == "public" || privacy == "followers" && relation == "accepted" ? 1 : 0, profile.Items.Count);
                Assert.All(profile.Items, item => Assert.Equal(owner, item.UserId));
                Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, owner, runnerId: owner)).Items);
            }
        }
        Assert.True(await conn.ExecuteScalarAsync<bool>("select relrowsecurity from pg_class where oid = 'follows'::regclass", transaction: tx));
        foreach (var role in new[] { "anon", "authenticated" })
            Assert.False(await conn.ExecuteScalarAsync<bool>("select has_table_privilege(@role, 'follows', 'SELECT,INSERT,UPDATE,DELETE')", new { role }, tx));
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Map_previews_hide_start_end_zones_and_never_bridge_return_visits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@owner), (@viewer);
            insert into users (id, display_name) values (@owner, 'Owner'), (@viewer, 'Viewer');
            insert into follows (follower_id, following_id, accepted_at) values (@viewer, @owner, now());
            insert into activities (id, user_id, source, started_at, duration_s, distance_m, privacy, route)
            values (@id, @owner, 'manual', now(), 1800, 5000, 'followers',
                st_geogfromtext('LINESTRING(0 0,0.02 0,0.0001 0.001,0.02 0.002,0.04 0.002)'))
            """, new { owner, viewer, id }, tx);
        var own = Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, owner)).Items);
        var shared = Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, viewer)).Items);
        Assert.NotNull(own.RouteGeoJson);
        Assert.NotNull(shared.RouteGeoJson);
        Assert.NotEqual(own.RouteGeoJson, shared.RouteGeoJson);
        using var route = System.Text.Json.JsonDocument.Parse(shared.RouteGeoJson);
        Assert.Equal("MultiLineString", route.RootElement.GetProperty("type").GetString());
        foreach (var endpoint in new[] { "st_startpoint", "st_endpoint" })
        {
            // Check the entire preview geometry, not just vertices: a chord must not expose a hidden zone.
            var distance = await conn.ExecuteScalarAsync<double>($"""
                select st_distance(st_geomfromgeojson(@json)::geography, {endpoint}(route::geometry)::geography)
                from activities where id = @id
                """, new { id, json = shared.RouteGeoJson }, tx);
            Assert.True(distance >= 500, $"Preview entered protected zone: {distance} m");
        }
        await conn.ExecuteAsync("insert into privacy_places (user_id, name, latitude, longitude, radius_m) values (@owner, 'Synthetic private place', 0.002, 0.02, 200)", new { owner }, tx);
        var masked = Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, viewer)).Items);
        var placeDistance = await conn.ExecuteScalarAsync<double>("select st_distance(st_geomfromgeojson(@json)::geography, st_setsrid(st_makepoint(0.02, 0.002), 4326)::geography)", new { json = masked.RouteGeoJson }, tx);
        Assert.True(placeDistance >= 200);
        Assert.Equal(own.RouteGeoJson, Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, owner)).Items).RouteGeoJson);
        await conn.ExecuteAsync("update activities set route = st_geogfromtext('LINESTRING(0 0,0.001 0)') where id = @id", new { id }, tx);
        Assert.Null(Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, viewer)).Items).RouteGeoJson);
        Assert.NotNull(Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, owner)).Items).RouteGeoJson);
        await conn.ExecuteAsync("update activities set route = null where id = @id", new { id }, tx);
        Assert.Null(Assert.Single((await SocialEndpoints.ReadFeedAsync(conn, owner)).Items).RouteGeoJson);
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Follow_request_approval_feed_shared_detail_and_revocation_work_over_http()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var viewer = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var ownerEmail = $"owner-{suffix}@example.com";
        var viewerEmail = $"viewer-{suffix}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, ownerEmail, $"Owner {suffix}", ct);
            await TestSession.CreateRunnerAsync(factory, viewer, conn, viewerEmail, "Viewer", ct);
            var ownerId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @ownerEmail", new { ownerEmail });
            var viewerId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @viewerEmail", new { viewerEmail });
            var activityId = Guid.NewGuid();
            await conn.ExecuteAsync("""
                insert into activities (id, user_id, source, title, started_at, duration_s, distance_m, privacy, avg_hr, max_hr, device)
                values (@activityId, @ownerId, 'manual', 'Shared test', now(), 1800, 5000, 'followers', 140, 170, 'Private watch');
                update activities set route = st_geogfromtext('LINESTRING(18 59,18.02 59.01,18.04 59.02)') where id = @activityId;
                insert into activity_streams (activity_id, t, dist, hr, lat, lon)
                values (@activityId, '{0,1800}', '{0,5000}', '{140,150}', '{59,59.1}', '{18,18.1}');
                insert into activity_laps (activity_id, lap_index, start_s, duration_s, distance_m, avg_hr, max_hr)
                values (@activityId, 0, 0, 1800, 5000, 140, 170);
                insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
                values (@activityId, @ownerId, 1000, 360, 0, now())
                """, new { activityId, ownerId });
            Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync($"/api/feed?beforeId={activityId}", ct)).StatusCode);
            using var anonymous = factory.CreateClient();
            foreach (var path in new[] { "/api/feed", "/api/follows", $"/api/runners/{ownerId}", $"/api/runners/{ownerId}/activities", $"/api/activities/{activityId}" })
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
            // No directory or search endpoint may disclose runner names, even to signed-in users.
            foreach (var client in new[] { anonymous, viewer })
                foreach (var path in new[] { "/api/runners", $"/api/runners?q={suffix}" })
                    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, ct)).StatusCode);
            var profile = (await viewer.GetFromJsonAsync<Runner>($"/api/runners/{ownerId}", ct))!;
            Assert.Equal(ownerId, profile.Id);
            Assert.Equal($"Owner {suffix}", profile.DisplayName);
            Assert.Equal("none", profile.Status);
            Assert.Empty((await viewer.GetFromJsonAsync<FeedPage>($"/api/runners/{ownerId}/activities", ct))!.Items);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/runners/{Guid.NewGuid()}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/runners/{Guid.NewGuid()}/activities", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync($"/api/runners/{ownerId}/activities?beforeId={activityId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.PostAsync($"/api/following/{viewerId}", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.PostAsync($"/api/following/{Guid.NewGuid()}", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsync($"/api/following/{ownerId}", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsync($"/api/following/{ownerId}", null, ct)).StatusCode);
            Assert.Equal("pending", Assert.Single((await owner.GetFromJsonAsync<FollowLists>("/api/follows", ct))!.Followers).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.PutAsync($"/api/followers/{ownerId}", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/activities/{activityId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/activities/{activityId}/best-efforts", ct)).StatusCode);
            Assert.Empty((await viewer.GetFromJsonAsync<FeedPage>("/api/feed", ct))!.Items);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/followers/{viewerId}", null, ct)).StatusCode);
            // Repeated requests cannot reset an approved relationship.
            Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsync($"/api/following/{ownerId}", null, ct)).StatusCode);
            Assert.Equal("accepted", Assert.Single((await viewer.GetFromJsonAsync<FollowLists>("/api/follows", ct))!.Following).Status);
            var item = Assert.Single((await viewer.GetFromJsonAsync<FeedPage>("/api/feed", ct))!.Items);
            Assert.Equal(activityId, item.Id);
            Assert.Equal(ownerId, item.UserId);
            Assert.NotNull(item.RouteGeoJson);
            Assert.Equal(activityId, Assert.Single((await viewer.GetFromJsonAsync<FeedPage>($"/api/runners/{ownerId}/activities", ct))!.Items).Id);
            Assert.Equal("accepted", (await viewer.GetFromJsonAsync<Runner>($"/api/runners/{ownerId}", ct))!.Status);
            var detail = (await viewer.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activityId}", ct))!;
            Assert.Null(detail.AvgHr);
            Assert.Null(detail.MaxHr);
            Assert.Null(detail.Device);
            Assert.Null(detail.Streams!.Hr);
            Assert.Null(detail.Streams.Lat);
            Assert.Null(detail.Streams.Lon);
            Assert.Null(Assert.Single(detail.Laps).AvgHr);
            Assert.Null(Assert.Single(detail.Laps).MaxHr);
            Assert.Empty(detail.EquipmentIds);
            var ownDetail = (await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activityId}", ct))!;
            Assert.Equal((short)140, ownDetail.AvgHr);
            Assert.NotNull(ownDetail.Streams!.Lat);
            Assert.Single((await viewer.GetFromJsonAsync<StoredBestEffort[]>($"/api/activities/{activityId}/best-efforts", ct))!);
            var update = new { title = "Shared test", tags = Array.Empty<string>(), equipmentIds = Array.Empty<Guid>(), privacy = "public" };
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.PutAsJsonAsync($"/api/activities/{activityId}", update, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.DeleteAsync($"/api/activities/{activityId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.PostAsync($"/api/activities/{activityId}/best-efforts/recalculate", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/followers/{viewerId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/activities/{activityId}", ct)).StatusCode);
            Assert.Empty((await viewer.GetFromJsonAsync<FeedPage>("/api/feed", ct))!.Items);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/activities/{activityId}", update, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/activities/{activityId}", ct)).StatusCode);
            Assert.Equal(activityId, Assert.Single((await viewer.GetFromJsonAsync<FeedPage>($"/api/runners/{ownerId}/activities", ct))!.Items).Id);
            Assert.Equal("none", (await viewer.GetFromJsonAsync<Runner>($"/api/runners/{ownerId}", ct))!.Status);
            // Public links work for signed-in runners, but do not turn the feed into a global directory.
            Assert.Empty((await viewer.GetFromJsonAsync<FeedPage>("/api/feed", ct))!.Items);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/activities/{activityId}", update with { privacy = "invalid" }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/activities/{activityId}", update with { privacy = "private" }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/activities/{activityId}", ct)).StatusCode);
            // Incoming rejection and outgoing cancellation both remove pending requests.
            await viewer.PostAsync($"/api/following/{ownerId}", null, ct);
            await owner.DeleteAsync($"/api/followers/{viewerId}", ct);
            Assert.Empty((await viewer.GetFromJsonAsync<FollowLists>("/api/follows", ct))!.Following);
            await viewer.PostAsync($"/api/following/{ownerId}", null, ct);
            await viewer.DeleteAsync($"/api/following/{ownerId}", ct);
            Assert.Empty((await owner.GetFromJsonAsync<FollowLists>("/api/follows", ct))!.Followers);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@ownerEmail, @viewerEmail)", new { ownerEmail, viewerEmail });
        }
    }

    [Fact]
    public async Task Feed_query_pages_equal_timestamps_and_survives_deleted_cursor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var email = $"feed-page-{Guid.NewGuid():N}@example.com";
        try
        {
            var userId = Guid.NewGuid();
            await conn.ExecuteAsync("insert into auth.users (id, email) values (@userId, @email); insert into users (id, display_name) values (@userId, 'Pagination runner')", new { userId, email });
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m)
                select @userId, 'manual', '2026-10-01T12:00:00Z'::timestamptz, 1800, 5000 from generate_series(1, 26)
                """, new { userId });
            var first = await SocialEndpoints.ReadFeedAsync(conn, userId);
            Assert.Equal(25, first.Items.Count);
            Assert.NotNull(first.NextCursor);
            await conn.ExecuteAsync("delete from activities where id = @id", new { first.NextCursor.Id });
            var cursor = first.NextCursor;
            var second = await SocialEndpoints.ReadFeedAsync(conn, userId, new DateTimeOffset(cursor.StartedAt), cursor.Id);
            var profilePage = await SocialEndpoints.ReadFeedAsync(conn, userId, new DateTimeOffset(cursor.StartedAt), cursor.Id, userId);
            Assert.Equal(second.Items, profilePage.Items);
            Assert.Single(second.Items);
            Assert.Null(second.NextCursor);
            Assert.DoesNotContain(second.Items[0].Id, first.Items.Select(a => a.Id));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }
}
