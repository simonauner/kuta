using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityPrivacyTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task All_creation_paths_read_current_default_without_changing_existing_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var store = factory.Services.GetRequiredService<RawFileStore>();
        var userId = Guid.NewGuid();
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@userId);
            insert into users (id, display_name) values (@userId, 'Default runner')
            """, new { userId });
        try
        {
            var request = new ManualActivityRequest("running", null, DateTimeOffset.UtcNow, 60, 200, null, null);
            var first = await ActivityStorage.SaveManualAsync(conn, userId, request, ct);
            Assert.Equal("private", await Privacy(first!.Id));
            foreach (var privacy in new[] { "followers", "public" })
            {
                await conn.ExecuteAsync("update users set default_activity_privacy = @privacy where id = @userId", new { privacy, userId });
                var manual = await ActivityStorage.SaveManualAsync(conn, userId, request, ct);
                Assert.Equal(privacy, await Privacy(manual!.Id));
                foreach (var source in new[] { "fit", "strava" })
                {
                    var parsed = FitImport.Parse(FitFile.Run(DateTime.UtcNow, 60))!;
                    var id = await ActivityStorage.SaveAsync(conn, store, userId, parsed, source, null,
                        source == "strava" ? $"{privacy}-import" : null, null, ".fit", ct);
                    Assert.Equal(privacy, await Privacy(id!.Value));
                }
            }
            Assert.Equal("private", await Privacy(first.Id));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }

        Task<string> Privacy(Guid id) => conn.QuerySingleAsync<string>("select privacy from activities where id = @id", new { id });
    }

    [Fact]
    public async Task Settings_persist_and_bulk_changes_only_own_activities_with_immediate_read_revocation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var viewer = factory.CreateClient();
        var ownerEmail = $"privacy-owner-{Guid.NewGuid():N}@example.com";
        var viewerEmail = $"privacy-viewer-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, ownerEmail, "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, viewer, conn, viewerEmail, "Viewer", ct);
            var ownerMe = (await owner.GetFromJsonAsync<Me>("/api/me", ct))!;
            var viewerMe = (await viewer.GetFromJsonAsync<Me>("/api/me", ct))!;
            Assert.Equal("private", ownerMe.DefaultActivityPrivacy);
            Assert.Equal(0, ownerMe.ActivitiesWithOtherPrivacy);
            var ownerId = ownerMe.Id;
            var viewerId = viewerMe.Id;
            var ids = (await conn.QueryAsync<Guid>("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, privacy)
                values (@ownerId, 'manual', now(), 60, 200, 'private'),
                       (@ownerId, 'manual', now(), 60, 200, 'followers'),
                       (@viewerId, 'manual', now(), 60, 200, 'private') returning id
                """, new { ownerId, viewerId })).ToArray();
            var response = await owner.PutAsJsonAsync("/api/me/activity-privacy", new { privacy = "public" }, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updatedMe = (await response.Content.ReadFromJsonAsync<Me>(ct))!;
            Assert.Equal("public", updatedMe.DefaultActivityPrivacy);
            Assert.Equal(2, updatedMe.ActivitiesWithOtherPrivacy);
            Assert.Equal("public", (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.DefaultActivityPrivacy);
            Assert.Equal("private", await Privacy(ids[0]));
            Assert.Equal("followers", await Privacy(ids[1]));

            response = await owner.PutAsJsonAsync("/api/me/activity-privacy", new { privacy = "public", applyToAll = true }, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await response.Content.ReadFromJsonAsync<Me>(ct))!.ActivitiesWithOtherPrivacy);
            Assert.Equal(0, (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.ActivitiesWithOtherPrivacy);
            Assert.Equal("public", await Privacy(ids[0]));
            Assert.Equal("public", await Privacy(ids[1]));
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/activities/{ids[0]}", ct)).StatusCode);
            response = await owner.PutAsJsonAsync("/api/me/activity-privacy", new { privacy = "private", applyToAll = true }, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("private", await Privacy(ids[0]));
            Assert.Equal("private", await Privacy(ids[1]));
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/activities/{ids[0]}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.PutAsJsonAsync($"/api/activities/{ids[0]}",
                new { title = "Intruder", tags = Array.Empty<string>(), equipmentIds = Array.Empty<Guid>(), privacy = "public" }, ct)).StatusCode);
            Assert.Equal("private", await Privacy(ids[2]));
            Assert.Equal("private", (await viewer.GetFromJsonAsync<Me>("/api/me", ct))!.DefaultActivityPrivacy);
            response = await owner.PutAsJsonAsync("/api/me/activity-privacy", new { privacy = "invalid", applyToAll = true }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalidPrivacy", (await response.Content.ReadFromJsonAsync<ApiError>(ct))!.Code);
            Assert.Equal("private", await Privacy(ids[0]));
            await conn.ExecuteAsync("update activities set privacy = 'followers' where id = @id", new { id = ids[0] });
            Assert.Equal(1, (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.ActivitiesWithOtherPrivacy);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@ownerEmail, @viewerEmail)", new { ownerEmail, viewerEmail });
        }

        Task<string> Privacy(Guid id) => conn.QuerySingleAsync<string>("select privacy from activities where id = @id", new { id });
    }
}
