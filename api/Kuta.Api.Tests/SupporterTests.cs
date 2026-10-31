using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class SupporterTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Badge_follows_its_privacy_accepted_follows_and_expiry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var (owner, follower, pending, stranger) = (ids[0], ids[1], ids[2], ids[3]);
        try
        {
            foreach (var id in ids)
                await conn.ExecuteAsync("""
                    insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')
                    """, new { id });
            await conn.ExecuteAsync("""
                insert into follows (follower_id, following_id, accepted_at) values (@follower, @owner, now()), (@pending, @owner, null);
                update users set supporter_tier = 'gold', supporter_until = now() + interval '1 year' where id = @owner
                """, new { owner, follower, pending });

            async Task<string?[]> Visible(string privacy)
            {
                await conn.ExecuteAsync("update users set supporter_badge_privacy = @privacy where id = @owner", new { privacy, owner });
                var tiers = new List<string?>();
                foreach (var userId in ids)
                    tiers.Add(await conn.ExecuteScalarAsync<string?>(
                        $"select {Supporters.VisibleTier} from users u where u.id = @owner", new { owner, userId }));
                return [.. tiers];
            }
            Assert.Equal(["gold", null, null, null], await Visible("private"));
            Assert.Equal(["gold", "gold", null, null], await Visible("followers"));
            Assert.Equal(["gold", "gold", "gold", "gold"], await Visible("public"));

            await conn.ExecuteAsync("update users set supporter_until = now() - interval '1 second' where id = @owner", new { owner });
            Assert.Equal([null, null, null, null], await Visible("public"));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids }); }
    }

    [Fact]
    public async Task Http_owner_sets_badge_privacy_and_others_see_it_on_the_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        var emails = new[] { $"supporter-{Guid.NewGuid():N}@example.com", $"supporter-viewer-{Guid.NewGuid():N}@example.com" };
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, emails[0], "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, other, conn, emails[1], "Viewer", ct);
            var ownerId = (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.Id;
            await conn.ExecuteAsync("update users set supporter_tier = 'silver', supporter_until = now() + interval '1 year' where id = @ownerId",
                new { ownerId });
            Assert.Null((await other.GetFromJsonAsync<Runner>($"/api/runners/{ownerId}", ct))!.SupporterTier);

            using var response = await owner.PutAsJsonAsync("/api/me/supporter-badge-privacy", new { privacy = "public" }, ct);
            var me = (await response.Content.ReadFromJsonAsync<Me>(ct))!;
            Assert.Equal(("silver", "public"), (me.Supporter.Tier, me.Supporter.BadgePrivacy));
            Assert.Equal("silver", (await other.GetFromJsonAsync<Runner>($"/api/runners/{ownerId}", ct))!.SupporterTier);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await owner.PutAsJsonAsync("/api/me/supporter-badge-privacy", new { privacy = "friends" }, ct)).StatusCode);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = any(@emails)", new { emails }); }
    }
}
