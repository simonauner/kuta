using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class PublicProfileTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Visitors_without_account_see_only_public_activities_of_runners_who_opted_in()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var store = factory.Services.GetRequiredService<RawFileStore>();
        using var owner = factory.CreateClient();
        using var visitor = factory.CreateClient();
        var email = $"public-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, email, "Public runner", ct);
            var ownerId = (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.Id;
            await conn.ExecuteAsync("update users set hr_consent_at = now() where id = @ownerId", new { ownerId });
            var ids = new Dictionary<string, Guid>();
            foreach (var (privacy, hour) in new[] { ("public", 6), ("followers", 7), ("private", 8) })
            {
                var parsed = FitImport.Parse(FitFile.Run(new DateTime(2026, 10, 1, hour, 0, 0, DateTimeKind.Utc), 600))!;
                ids[privacy] = (await ActivityStorage.SaveAsync(conn, store, ownerId, parsed, "fit", null, null, null, ".fit",
                    ct, privacy: privacy))!.Value;
            }

            // Off by default: the link reveals nothing, not even that the runner exists
            Assert.False((await owner.GetFromJsonAsync<Me>("/api/me", ct))!.PublicProfile);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/public/runners/{ownerId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/public/runners/{ownerId}/activities", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/public/activities/{ids["public"]}", ct)).StatusCode);

            var response = await owner.PutAsJsonAsync("/api/me/public-profile", new { enabled = true }, ct);
            Assert.True((await response.Content.ReadFromJsonAsync<Me>(ct))!.PublicProfile);

            var runner = await visitor.GetFromJsonAsync<Runner>($"/api/public/runners/{ownerId}", ct);
            Assert.Equal("Public runner", runner!.DisplayName);
            var feed = await visitor.GetFromJsonAsync<FeedPage>($"/api/public/runners/{ownerId}/activities", ct);
            Assert.Equal(ids["public"], Assert.Single(feed!.Items).Id);

            // The same filtering as for signed-in viewers: no heart rate, device or GPS streams
            var detail = await visitor.GetFromJsonAsync<ActivityDetail>($"/api/public/activities/{ids["public"]}", ct);
            Assert.Equal((null, null, null, null), (detail!.AvgHr, detail.Device, detail.Streams?.Hr, detail.Streams?.Lat));
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/public/activities/{ids["followers"]}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/public/activities/{ids["private"]}", ct)).StatusCode);
            // The signed-in API stays closed to visitors
            Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync($"/api/activities/{ids["public"]}", ct)).StatusCode);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }
}
