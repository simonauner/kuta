using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class RoutesTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    // At latitude 59, 0.0001° of latitude is about 11 m; the target runs 2.9 km east.
    const string Target = "LINESTRING(18 59,18.05 59)";

    static Task<Guid> Insert(NpgsqlConnection conn, Guid userId, string sport, string? route, int hoursAgo, int durationS = 900) =>
        conn.ExecuteScalarAsync<Guid>("""
            insert into activities (user_id, source, sport, started_at, duration_s, distance_m, route)
            values (@userId, 'manual', @sport, now() - make_interval(hours => @hoursAgo), @durationS, 2900, st_geogfromtext(@route))
            returning id
            """, new { userId, sport, route, hoursAgo, durationS });

    static async Task<(Guid Owner, Guid Other)> CreateUsers(NpgsqlConnection conn)
    {
        var (owner, other) = (Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@owner), (@other);
            insert into users (id, display_name) values (@owner, 'Owner'), (@other, 'Other');
            """, new { owner, other });
        return (owner, other);
    }

    [Fact]
    public async Task Groups_own_activities_on_the_same_route_in_the_same_direction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var (owner, other) = await CreateUsers(conn);
        try
        {
            var target = await Insert(conn, owner, "running", Target, 1);
            // 11 m to the side with other vertices, and a trail run in the same group
            var shifted = await Insert(conn, owner, "running", "LINESTRING(18 59.0001,18.02 59.0001,18.05 59.0001)", 2);
            var trail = await Insert(conn, owner, "trail run", Target, 3);
            await Insert(conn, owner, "running", "LINESTRING(18.05 59,18 59)", 4); // reversed
            await Insert(conn, owner, "running", "LINESTRING(18 59.0005,18.05 59.0005)", 5); // parallel road 55 m away
            await Insert(conn, owner, "running", "LINESTRING(18 59,18.025 59.002,18.05 59)", 6); // same ends, 220 m detour
            await Insert(conn, owner, "running", "LINESTRING(18 59,18.045 59)", 7); // 10 % shorter
            await Insert(conn, owner, "cycling", Target, 8);
            await Insert(conn, owner, "running", null, 9);
            var stranger = await Insert(conn, other, "running", Target, 1);

            var matches = await Routes.ReadSimilarAsync(conn, target, owner, ct);
            Assert.Equal([target, shifted, trail], matches!.Select(m => m.Id));
            Assert.Null(await Routes.ReadSimilarAsync(conn, target, other, ct));
            Assert.Equal([stranger], (await Routes.ReadSimilarAsync(conn, stranger, other, ct))!.Select(m => m.Id));
            // Each activity with a line has one route; the oldest on a course is the reference
            Assert.Equal(6, await conn.ExecuteScalarAsync<int>("select count(*) from routes where user_id = @owner", new { owner }));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@owner, @other)", new { owner, other });
        }
    }

    [Fact]
    public async Task Top_routes_follow_sport_changes_and_deleted_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var (owner, other) = await CreateUsers(conn);
        try
        {
            const string loop = "LINESTRING(18 59,18.02 59,18.02 59.01,18 59)";
            var latest = await Insert(conn, owner, "running", Target, 1, durationS: 950);
            await Insert(conn, owner, "running", Target, 2, durationS: 880);
            var bike = await Insert(conn, owner, "running", Target, 3);
            await Insert(conn, owner, "running", loop, 4);
            await Insert(conn, owner, "running", loop, 5);
            await Insert(conn, owner, "running", "LINESTRING(18 59,18 59.03)", 6); // run once: not listed
            await Insert(conn, other, "running", Target, 1);

            var top = await Routes.ReadTopAsync(conn, owner, ct);
            Assert.Equal([3, 2], top.Select(r => r.Count));
            Assert.Equal(("running", 880, latest), (top[0].SportGroup, top[0].BestDurationS, top[0].LatestActivityId));

            // A changed sport group moves the activity to a route of that group
            await conn.ExecuteAsync("update activities set sport = 'cycling', route_id = null where id = @bike", new { bike });
            Assert.Equal([2, 2], (await Routes.ReadTopAsync(conn, owner, ct)).Select(r => r.Count));

            // Deleting all activities leaves no copy of their lines
            await conn.ExecuteAsync("delete from activities where user_id = @owner", new { owner });
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("select count(*) from routes where user_id = @owner", new { owner }));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@owner, @other)", new { owner, other });
        }
    }

    [Fact]
    public async Task Only_the_owner_reads_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var follower = factory.CreateClient();
        var ownerEmail = $"routes-owner-{Guid.NewGuid():N}@example.com";
        var followerEmail = $"routes-follower-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, ownerEmail, "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, follower, conn, followerEmail, "Follower", ct);
            var id = await conn.ExecuteScalarAsync<Guid>("""
                insert into follows (follower_id, following_id, accepted_at)
                select f.id, o.id, now() from auth.users f, auth.users o where f.email = @followerEmail and o.email = @ownerEmail;
                with inserted as (
                    insert into activities (user_id, source, sport, started_at, duration_s, distance_m, privacy, route)
                    select id, 'manual', 'running', now() - make_interval(days => n), 900, 2900, 'public', st_geogfromtext(@route)
                    from auth.users, generate_series(1, 2) n where email = @ownerEmail
                    returning id
                )
                select id from inserted limit 1
                """, new { ownerEmail, followerEmail, route = Target });
            Assert.Equal(2, (await owner.GetFromJsonAsync<SimilarRouteActivity[]>($"/api/activities/{id}/similar", ct))!.Length);
            Assert.Equal(2, Assert.Single((await owner.GetFromJsonAsync<RouteSummary[]>("/api/routes", ct))!).Count);
            // Even public activities: routes are grouped over the owner's private activities.
            Assert.Equal(HttpStatusCode.NotFound, (await follower.GetAsync($"/api/activities/{id}/similar", ct)).StatusCode);
            Assert.Empty((await follower.GetFromJsonAsync<RouteSummary[]>("/api/routes", ct))!);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@ownerEmail, @followerEmail)", new { ownerEmail, followerEmail });
        }
    }
}
