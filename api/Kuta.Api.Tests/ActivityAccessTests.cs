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
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activities/years", ct)).StatusCode);
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

                var activities = await client.GetFromJsonAsync<ActivityPage>("/api/activities", ct);

                var activity = Assert.Single(activities!.Items);
                Assert.Equal(activityIds[i], activity.Id);
                Assert.Equal($"Private run {i}", activity.Title);
                Assert.Null(activities.NextCursor);
                var years = await client.GetFromJsonAsync<int[]>("/api/activities/years?timeZone=UTC", ct);
                Assert.Equal(new[] { activity.StartedAt.Year }, years);
                var filtered = await client.GetFromJsonAsync<ActivityPage>($"/api/activities?year={activity.StartedAt.Year}", ct);
                Assert.Equal(activity.Id, Assert.Single(filtered!.Items).Id);
                Assert.False(activity.ShowMonth);
                Assert.False(activity.ShowWeek);
                var older = await client.GetFromJsonAsync<ActivityPage>(
                    $"/api/activities?timeZone=Europe%2FStockholm&beforeStartedAt={Uri.EscapeDataString(activity.StartedAt.ToString("O"))}&beforeId={activity.Id}", ct);
                Assert.Empty(older!.Items);
                if (i == 0)
                {
                    var invalidYear = await client.GetAsync("/api/activities?year=9999", ct);
                    Assert.Equal(HttpStatusCode.BadRequest, invalidYear.StatusCode);
                    Assert.Equal("invalidActivityYear", (await invalidYear.Content.ReadFromJsonAsync<ApiError>(ct))!.Code);
                    var invalidYearsZone = await client.GetAsync("/api/activities/years?timeZone=NotAZone", ct);
                    Assert.Equal(HttpStatusCode.BadRequest, invalidYearsZone.StatusCode);
                    var missingId = await client.GetAsync("/api/activities?beforeStartedAt=2026-01-01T00:00:00Z", ct);
                    Assert.Equal(HttpStatusCode.BadRequest, missingId.StatusCode);
                    Assert.Equal("invalidActivityPage", (await missingId.Content.ReadFromJsonAsync<ApiError>(ct))!.Code);
                    var invalidZone = await client.GetAsync("/api/activities?timeZone=NotAZone", ct);
                    Assert.Equal(HttpStatusCode.BadRequest, invalidZone.StatusCode);
                    Assert.Equal("invalidTimeZone", (await invalidZone.Content.ReadFromJsonAsync<ApiError>(ct))!.Code);
                }
            }
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id = any(@userIds)", new { userIds });
        }
    }
}
