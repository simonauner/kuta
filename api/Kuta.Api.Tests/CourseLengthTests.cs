using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class CourseLengthTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Editing_keeps_the_course_length_for_orienteering_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var client = factory.CreateClient();
        await TestSession.CreateRunnerAsync(factory, client, conn, $"course-{Guid.NewGuid():N}@example.com", "Runner", ct);
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/me", ct)).GetProperty("id").GetGuid();
        var id = await conn.ExecuteScalarAsync<Guid>("""
            insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
            values (@userId, 'manual', 'orienteering', '2026-05-03T10:00:00Z', 3000, 6000) returning id
            """, new { userId });

        Task<HttpResponseMessage> Edit(string sport, double courseLengthM) => client.PutAsJsonAsync($"/api/activities/{id}",
            new { sport, courseLengthM, tags = Array.Empty<string>(), equipmentIds = Array.Empty<Guid>() }, ct);
        async Task<JsonElement> CourseLength() =>
            (await client.GetFromJsonAsync<JsonElement>($"/api/activities/{id}", ct)).GetProperty("courseLengthM");

        Assert.Equal(HttpStatusCode.BadRequest, (await Edit("orienteering", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Edit("orienteering", 5200)).StatusCode);
        Assert.Equal(5200, (await CourseLength()).GetDouble());
        Assert.Equal(HttpStatusCode.NoContent, (await Edit("running", 5200)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await CourseLength()).ValueKind);
    }
}
