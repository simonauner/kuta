using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class RanTogetherTests
{
    static readonly DateTime Start = new(2026, 5, 3, 7, 0, 0, DateTimeKind.Utc);

    // Northwards at 3 m/s from a starting point, one sample per second; eastM moves the whole line sideways.
    static CompanionTrack Run(int startOffsetS, int durationS, double eastM = 0, double startNorthM = 0)
    {
        var t = Enumerable.Range(0, durationS).ToArray();
        return new CompanionTrack(Start.AddSeconds(startOffsetS), t,
            t.Select(s => (double?)(59.3 + (startNorthM + 3.0 * s) / 111_320)).ToArray(),
            t.Select(_ => (double?)(18.0 + eastM / (111_320 * Math.Cos(double.DegreesToRadians(59.3))))).ToArray());
    }

    [Fact]
    public void Same_place_at_the_same_time_is_together() =>
        Assert.True(Companions.RanTogether(Run(0, 3600), Run(0, 3600, eastM: 20)));

    [Fact]
    public void Same_line_an_hour_apart_is_not_together() =>
        Assert.False(Companions.RanTogether(Run(0, 3600), Run(3600, 3600)));

    [Fact]
    public void A_parallel_street_is_not_together() =>
        Assert.False(Companions.RanTogether(Run(0, 3600), Run(0, 3600, eastM: 300)));

    // Joining for 15 minutes of an hour: near the whole time both were recording.
    [Fact]
    public void Joining_part_of_the_way_is_together() =>
        Assert.True(Companions.RanTogether(Run(0, 3600), Run(1200, 900, startNorthM: 3600)));

    [Fact]
    public void A_few_minutes_side_by_side_is_not_enough() =>
        Assert.False(Companions.RanTogether(Run(0, 3600), Run(1200, 300, startNorthM: 3600)));

    // Same race, different pace: near only at the start.
    [Fact]
    public void Same_course_at_different_speeds_is_not_together()
    {
        var slow = Run(0, 3600);
        var fast = new CompanionTrack(Start, slow.T, slow.T.Select(s => (double?)(59.3 + 4.0 * s / 111_320)).ToArray(), slow.Lon);
        Assert.False(Companions.RanTogether(slow, fast));
    }

    [Fact]
    public void Without_gps_nothing_is_together() =>
        Assert.False(Companions.RanTogether(Run(0, 3600), new CompanionTrack(Start, [0, 1, 2], null, null)));
}

public class CompanionsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    // Two runners side by side; who sees the link depends only on who may read both activities and on the opt-out.
    [Fact]
    public async Task Link_shows_only_when_the_viewer_may_read_both_and_neither_owner_opted_out()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var anna = factory.CreateClient();
        using var viewer = factory.CreateClient();
        await TestSession.CreateRunnerAsync(factory, anna, conn, $"anna-{Guid.NewGuid():N}@example.com", "Anna", ct);
        await TestSession.CreateRunnerAsync(factory, viewer, conn, $"viewer-{Guid.NewGuid():N}@example.com", "Viewer", ct);
        var annaId = (await anna.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var viewerId = (await viewer.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var bo = Guid.NewGuid();
        var (annaRun, boRun) = (Guid.NewGuid(), Guid.NewGuid());
        // A unique place and time, so other tests' activities never come near.
        var start = new DateTime(2020, 1, 1, 6, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(1_000_000));
        var lat = 10 + Random.Shared.NextDouble() * 40;
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@bo);
            insert into users (id, display_name) values (@bo, 'Bo');
            insert into follows (follower_id, following_id, accepted_at) values (@viewerId, @annaId, now()), (@viewerId, @bo, now());
            insert into activities (id, user_id, source, sport, started_at, duration_s, distance_m, privacy, route)
            select id, owner, 'manual', 'running', @start, 1800, 5400, 'followers',
                st_makeline(st_makepoint(18, @lat), st_makepoint(18, @lat + 5400 / 111320.0))::geography
            from (values (@annaRun, @annaId), (@boRun, @bo)) v(id, owner);
            insert into activity_streams (activity_id, t, lat, lon)
            select id, array(select generate_series(0, 1799)),
                array(select @lat + 3.0 * s / 111320 from generate_series(0, 1799) s),
                array(select 18.0::double precision from generate_series(0, 1799))
            from (values (@annaRun), (@boRun)) v(id);
            """, new { bo, viewerId, annaId, annaRun, boRun, start, lat });

        async Task<string[]> Companions(HttpClient client, Guid activity) =>
            (await client.GetFromJsonAsync<JsonElement>($"/api/activities/{activity}/companions", ct))
            .EnumerateArray().Select(c => c.GetProperty("displayName").GetString()!).ToArray();

        // The viewer follows both; Anna does not follow Bo and cannot read his activity.
        Assert.Equal(["Bo"], await Companions(viewer, annaRun));
        Assert.Equal(["Anna"], await Companions(viewer, boRun));
        Assert.Empty(await Companions(anna, annaRun));

        await conn.ExecuteAsync("update activities set privacy = 'public' where id = @boRun", new { boRun });
        Assert.Equal(["Bo"], await Companions(anna, annaRun));

        // Anna opts out on her activity: no link in either direction.
        Assert.Equal(HttpStatusCode.NoContent, (await anna.PutAsJsonAsync($"/api/activities/{annaRun}",
            new { title = "Morning run", tags = Array.Empty<string>(), equipmentIds = Array.Empty<Guid>(), hideCompanions = true }, ct)).StatusCode);
        Assert.Empty(await Companions(viewer, annaRun));
        Assert.Empty(await Companions(viewer, boRun));

        await conn.ExecuteAsync("update activities set privacy = 'private' where id = @annaRun", new { annaRun });
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/activities/{annaRun}/companions", ct)).StatusCode);
    }
}
