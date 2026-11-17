using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class SameCourseTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    // Anna's public run is the course. The viewer and Bo (private) linked to it, Cia (public) linked to Bo's run.
    // The viewer only sees what they may read, and never Cia, who is only reachable through Bo's hidden run.
    [Fact]
    public async Task Course_is_followed_only_through_readable_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var (viewer, anna, bo, cia) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (own, annaRun, boRun, ciaRun) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into auth.users (id) select unnest(array[@viewer, @anna, @bo, @cia]);
            insert into users (id, display_name) values (@viewer, 'Viewer'), (@anna, 'Anna'), (@bo, 'Bo'), (@cia, 'Cia');
            insert into activities (id, user_id, source, sport, started_at, duration_s, distance_m, privacy, same_course_as)
            values (@annaRun, @anna, 'manual', 'orienteering', '2026-05-03T10:00:00Z', 3000, 6000, 'public', null),
                   (@own, @viewer, 'manual', 'orienteering', '2026-05-03T10:10:00Z', 3300, 6500, 'private', @annaRun),
                   (@boRun, @bo, 'manual', 'orienteering', '2026-05-03T10:20:00Z', 3100, 6100, 'private', @annaRun),
                   (@ciaRun, @cia, 'manual', 'orienteering', '2026-05-03T10:30:00Z', 3200, 6200, 'public', @boRun);
            insert into activity_laps (activity_id, lap_index, start_s, duration_s, distance_m)
            values (@annaRun, 0, 0, 1000, 2000), (@annaRun, 1, 1000, 2000, 4000), (@own, 0, 0, 3300, 6500);
            """, new { viewer, anna, bo, cia, own, annaRun, boRun, ciaRun });

        var course = await SameCourse.ReadAsync(conn, annaRun, viewer, ct);
        Assert.Equal([annaRun, own], course!.Select(r => r.ActivityId));
        Assert.Equal([1000, 2000], course[0].Legs.Select(l => l.DurationS));
        Assert.Equal([annaRun, own], (await SameCourse.ReadAsync(conn, own, viewer, ct))!.Select(r => r.ActivityId));
        Assert.Null(await SameCourse.ReadAsync(conn, boRun, viewer, ct));
        // Bo sees his own run and the public ones on both sides of it, but not the viewer's private run.
        Assert.Equal([annaRun, boRun, ciaRun], (await SameCourse.ReadAsync(conn, annaRun, bo, ct))!.Select(r => r.ActivityId));
        // Alone on a course is no comparison.
        await conn.ExecuteAsync("update activities set same_course_as = null where id = @own", new { own });
        Assert.Empty((await SameCourse.ReadAsync(conn, own, viewer, ct))!);
    }

    [Fact]
    public async Task Owner_links_to_a_readable_activity_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var anna = factory.CreateClient();
        using var viewer = factory.CreateClient();
        await TestSession.CreateRunnerAsync(factory, anna, conn, $"anna-{Guid.NewGuid():N}@example.com", "Anna", ct);
        await TestSession.CreateRunnerAsync(factory, viewer, conn, $"viewer-{Guid.NewGuid():N}@example.com", "Viewer", ct);
        var annaId = (await anna.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var viewerId = (await viewer.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var (own, annaRun) = (Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into activities (id, user_id, source, sport, started_at, duration_s, distance_m, privacy)
            values (@annaRun, @annaId, 'manual', 'orienteering', '2026-05-03T10:00:00Z', 3000, 6000, 'private'),
                   (@own, @viewerId, 'manual', 'orienteering', '2026-05-03T10:10:00Z', 3300, 6500, 'private');
            """, new { annaId, viewerId, own, annaRun });

        Task<HttpResponseMessage> Link(HttpClient client, Guid activity, Guid? target) =>
            client.PutAsJsonAsync($"/api/activities/{activity}/same-course", new { activityId = target }, ct);

        Assert.Equal(HttpStatusCode.NotFound, (await Link(viewer, own, annaRun)).StatusCode);
        await conn.ExecuteAsync("update activities set privacy = 'public' where id = @annaRun", new { annaRun });
        Assert.Equal(HttpStatusCode.NotFound, (await Link(viewer, annaRun, own)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Link(viewer, own, annaRun)).StatusCode);
        var course = await viewer.GetFromJsonAsync<JsonElement>($"/api/activities/{annaRun}/same-course", ct);
        Assert.Equal(["Anna", "Viewer"], course.EnumerateArray().Select(r => r.GetProperty("displayName").GetString()));
        Assert.Equal(annaRun, (await viewer.GetFromJsonAsync<JsonElement>($"/api/activities/{own}", ct))
            .GetProperty("sameCourseAs").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await Link(viewer, own, null)).StatusCode);
        Assert.Empty((await viewer.GetFromJsonAsync<JsonElement>($"/api/activities/{annaRun}/same-course", ct)).EnumerateArray());
    }
}
