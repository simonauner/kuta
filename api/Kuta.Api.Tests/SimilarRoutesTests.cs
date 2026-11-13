using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class SimilarRoutesTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    // At latitude 59, 0.0001° of latitude is about 11 m; the target runs 2.9 km east.
    const string Target = "LINESTRING(18 59,18.05 59)";

    static Task<Guid> Insert(NpgsqlConnection conn, NpgsqlTransaction tx, Guid userId, string sport, string? route, int hoursAgo) =>
        conn.ExecuteScalarAsync<Guid>("""
            insert into activities (user_id, source, sport, started_at, duration_s, distance_m, route)
            values (@userId, 'manual', @sport, now() - make_interval(hours => @hoursAgo), 900, 2900, st_geogfromtext(@route))
            returning id
            """, new { userId, sport, route, hoursAgo }, tx);

    [Fact]
    public async Task Matches_own_activities_on_the_same_route_in_the_same_direction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@owner), (@other);
            insert into users (id, display_name) values (@owner, 'Owner'), (@other, 'Other');
            """, new { owner, other }, tx);
        var target = await Insert(conn, tx, owner, "running", Target, 1);
        // 11 m to the side with other vertices, and a trail run in the same group
        var shifted = await Insert(conn, tx, owner, "running", "LINESTRING(18 59.0001,18.02 59.0001,18.05 59.0001)", 2);
        var trail = await Insert(conn, tx, owner, "trail run", Target, 3);
        await Insert(conn, tx, owner, "running", "LINESTRING(18.05 59,18 59)", 4); // reversed
        await Insert(conn, tx, owner, "running", "LINESTRING(18 59.0005,18.05 59.0005)", 5); // parallel road 55 m away
        await Insert(conn, tx, owner, "running", "LINESTRING(18 59,18.025 59.002,18.05 59)", 6); // same ends, 220 m detour
        await Insert(conn, tx, owner, "running", "LINESTRING(18 59,18.045 59)", 7); // 10 % shorter
        await Insert(conn, tx, owner, "cycling", Target, 8);
        await Insert(conn, tx, owner, "running", null, 9);
        var stranger = await Insert(conn, tx, other, "running", Target, 1);

        var matches = await SimilarRoutes.ReadAsync(conn, target, owner, ct);
        Assert.Equal([target, shifted, trail], matches!.Select(m => m.Id));
        Assert.Null(await SimilarRoutes.ReadAsync(conn, target, other, ct));
        Assert.Equal([stranger], (await SimilarRoutes.ReadAsync(conn, stranger, other, ct))!.Select(m => m.Id));
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Only_the_owner_can_read_similar_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var follower = factory.CreateClient();
        var ownerEmail = $"similar-owner-{Guid.NewGuid():N}@example.com";
        var followerEmail = $"similar-follower-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, ownerEmail, "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, follower, conn, followerEmail, "Follower", ct);
            var id = await conn.ExecuteScalarAsync<Guid>("""
                insert into follows (follower_id, following_id, accepted_at)
                select f.id, o.id, now() from auth.users f, auth.users o where f.email = @followerEmail and o.email = @ownerEmail;
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m, privacy, route)
                select id, 'manual', 'running', now(), 900, 2900, 'public', st_geogfromtext(@route)
                from auth.users where email = @ownerEmail
                returning id
                """, new { ownerEmail, followerEmail, route = Target });
            var items = await owner.GetFromJsonAsync<SimilarRouteActivity[]>($"/api/activities/{id}/similar", ct);
            Assert.Equal(id, Assert.Single(items!).Id);
            // Even a public activity: the comparison is over the owner's private activities.
            Assert.Equal(HttpStatusCode.NotFound, (await follower.GetAsync($"/api/activities/{id}/similar", ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@ownerEmail, @followerEmail)", new { ownerEmail, followerEmail });
        }
    }
}
