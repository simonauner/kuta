using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityAccessTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Each_user_can_read_only_their_own_private_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var activityIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            for (var i = 0; i < userIds.Length; i++)
            {
                await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)",
                    new { id = userIds[i], email = $"private-activity-{userIds[i]:N}@example.com" });
                await conn.ExecuteAsync("insert into users (id, display_name) values (@id, 'Synthetic runner')",
                    new { id = userIds[i] });
                await conn.ExecuteAsync("""
                    insert into activities (id, user_id, source, title, started_at, duration_s, distance_m, privacy)
                    values (@id, @userId, 'manual', @title, now(), 1800, 5000, 'private')
                    """, new { id = activityIds[i], userId = userIds[i], title = $"Private run {i}" });
            }

            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activities", ct)).StatusCode);
            for (var i = 0; i < userIds.Length; i++)
            {
                using var scope = factory.Services.CreateScope();
                var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
                await SupabaseCookieSession.SignInAsync(http, new AuthSession(userIds[i],
                    $"private-activity-{userIds[i]:N}@example.com", "synthetic-access", "synthetic-refresh",
                    DateTimeOffset.UtcNow.AddHours(1)), ct);
                using var client = factory.CreateClient();
                client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
                    http.Response.Headers.SetCookie.Select(cookie => cookie!.Split(';')[0])));

                var activities = await client.GetFromJsonAsync<ActivitySummary[]>("/api/activities", ct);

                var activity = Assert.Single(activities!);
                Assert.Equal(activityIds[i], activity.Id);
                Assert.Equal($"Private run {i}", activity.Title);
                Assert.Empty(activity.Tags);
                await TestSession.UseCsrfToken(client, ct);
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                    $"/api/activities/{activity.Id}/tags", new { tags = new[] { "race", "workout", "race" } }, ct)).StatusCode);
                var detail = await client.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct);
                Assert.Equal(new[] { "race", "workout" }, detail!.Tags);
                Assert.Equal(activity.Id, Assert.Single((await client.GetFromJsonAsync<ActivitySummary[]>(
                    "/api/activities?tag=race", ct))!).Id);
                Assert.Empty((await client.GetFromJsonAsync<ActivitySummary[]>("/api/activities?tag=easy", ct))!);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
                    $"/api/activities/{activityIds[1 - i]}", ct)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
                    $"/api/activities/{activityIds[1 - i]}/tags", new { tags = new[] { "easy" } }, ct)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
                    $"/api/activities/{activity.Id}/tags", new { tags = (string[]?)null }, ct)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/activities?tag=unknown", ct)).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                    $"/api/activities/{activity.Id}/tags", new { tags = Array.Empty<string>() }, ct)).StatusCode);
                Assert.Empty((await client.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct))!.Tags);

            }
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id = any(@userIds)", new { userIds });
        }
    }
}
