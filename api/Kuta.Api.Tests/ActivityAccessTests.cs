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
                await conn.ExecuteAsync("""
                    insert into activity_streams (activity_id, t, lat, lon)
                    values (@id, array[0, 60, 120], array[59.0, 59.01, 59.02], array[18.0, 18.01, 18.02])
                    """, new { id = activityIds[i] });
            }

            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activities", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activities/years", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activities/statistics?year=2026", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activities/statistics/days", ct)).StatusCode);
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

                var activities = await client.GetFromJsonAsync<ActivityPage>("/api/activities", ct);

                var activity = Assert.Single(activities!.Items);
                Assert.Equal(activityIds[i], activity.Id);
                Assert.Equal($"Private run {i}", activity.Title);
                Assert.Empty(activity.Tags);
                await TestSession.UseCsrfToken(client, ct);
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                    $"/api/activities/{activity.Id}", new { title = activity.Title, tags = new[] { "race", "workout", "race" }, equipmentIds = Array.Empty<Guid>() }, ct)).StatusCode);
                var detail = await client.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct);
                Assert.Equal(new[] { "race", "workout" }, detail!.Tags);
                Assert.Equal(new double?[] { 59, 59.01, 59.02 }, detail.Streams!.Lat);
                Assert.Equal(new double?[] { 18, 18.01, 18.02 }, detail.Streams.Lon);
                Assert.Equal(activity.Id, Assert.Single((await client.GetFromJsonAsync<ActivityPage>(
                    $"/api/activities?tag=race&year={activity.StartedAt.Year}", ct))!.Items).Id);
                Assert.Empty((await client.GetFromJsonAsync<ActivityPage>("/api/activities?tag=easy", ct))!.Items);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
                    $"/api/activities/{activityIds[1 - i]}", ct)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
                    $"/api/activities/{activityIds[1 - i]}", new { title = activity.Title, tags = new[] { "easy" }, equipmentIds = Array.Empty<Guid>() }, ct)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
                    $"/api/activities/{activity.Id}", new { title = activity.Title, tags = (string[]?)null, equipmentIds = Array.Empty<Guid>() }, ct)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/activities?tag=unknown", ct)).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                    $"/api/activities/{activity.Id}", new { title = activity.Title, tags = Array.Empty<string>(), equipmentIds = Array.Empty<Guid>() }, ct)).StatusCode);
                Assert.Empty((await client.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct))!.Tags);

                var statistics = await client.GetFromJsonAsync<ActivityStatisticsMonth[]>(
                    $"/api/activities/statistics?year={activity.StartedAt.Year}&sportGroup=running&timeZone=UTC", ct);
                Assert.Equal(12, statistics!.Length);
                Assert.Equal(1, statistics.Sum(m => m.Count));
                Assert.Equal(5000, statistics.Sum(m => m.DistanceM));
                var day = Assert.Single((await client.GetFromJsonAsync<DailyTotal[]>(
                    "/api/activities/statistics/days?sportGroup=running&timeZone=UTC", ct))!);
                Assert.Equal((DateOnly.FromDateTime(activity.StartedAt.ToUniversalTime()), 1, 5000d), (day.Day, day.Count, day.DistanceM));
                if (i == 0)
                {
                    foreach (var query in new[] { "year=0", "year=2026&sportGroup=trail%20run", "year=2026&timeZone=NotAZone" })
                        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/activities/statistics?{query}", ct)).StatusCode);
                    foreach (var query in new[] { "sportGroup=trail%20run", "timeZone=NotAZone" })
                        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/activities/statistics/days?{query}", ct)).StatusCode);
                }

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

    // The goal form warns when a visible goal counts activities the viewer may not see; the list covers own activities only.
    [Fact]
    public async Task Activity_privacies_lists_only_own_visibilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"privacies-a-{Guid.NewGuid():N}@example.com";
        var other = $"privacies-b-{Guid.NewGuid():N}@example.com";
        using var client = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await TestSession.CreateRunnerAsync(factory, client, conn, runner, "Löparen", ct);
            await TestSession.CreateRunnerAsync(factory, otherClient, conn, other, "Grannen", ct);
            Assert.Empty((await client.GetFromJsonAsync<string[]>("/api/activities/privacies", ct))!);
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, privacy)
                select id, 'manual', now(), 1800, 5000, p
                from auth.users, unnest(array['private', 'public', 'public']) p where email = @runner
                union all
                select id, 'manual', now(), 1800, 5000, 'followers' from auth.users where email = @other
                """, new { runner, other });
            Assert.Equal(["private", "public"], (await client.GetFromJsonAsync<string[]>("/api/activities/privacies", ct))!);
            Assert.Equal(["followers"], (await otherClient.GetFromJsonAsync<string[]>("/api/activities/privacies", ct))!);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner, @other)", new { runner, other });
        }
    }
}
