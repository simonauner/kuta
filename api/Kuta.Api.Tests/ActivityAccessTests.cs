using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
                using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
                    http.Response.Headers.SetCookie.Select(cookie => cookie!.Split(';')[0])));

                var activities = await client.GetFromJsonAsync<ActivitySummary[]>("/api/activities", ct);

                var activity = Assert.Single(activities!);
                Assert.Equal(activityIds[i], activity.Id);
                Assert.Equal($"Private run {i}", activity.Title);
            }
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id = any(@userIds)", new { userIds });
        }
    }
}
