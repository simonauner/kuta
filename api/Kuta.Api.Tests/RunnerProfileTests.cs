using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class RunnerProfileTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Summary_counts_only_activities_the_viewer_may_read()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var owner = Guid.NewGuid();
        var follower = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var at = (int hour, int minute) => day.ToDateTime(new(hour, minute), DateTimeKind.Utc);
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@owner), (@follower), (@stranger);
            insert into users (id, display_name) values (@owner, 'Owner'), (@follower, 'Follower'), (@stranger, 'Stranger');
            insert into follows (follower_id, following_id, accepted_at) values (@follower, @owner, now());
            insert into activities (user_id, source, started_at, duration_s, distance_m, privacy)
            values (@owner, 'manual', @late, 1800, 5000, 'private'),
                   (@owner, 'manual', @evening, 1800, 6000, 'followers'),
                   (@owner, 'manual', @dayBefore, 1800, 7000, 'public'),
                   (@owner, 'manual', now() - interval '400 days', 1800, 8000, 'public')
            """, new { owner, follower, stranger, late = at(23, 30), evening = at(20, 0), dayBefore = at(8, 0).AddDays(-1) }, tx);
        async Task<IReadOnlyList<CalendarDay>> Days(Guid viewer) =>
            (await RunnerProfile.ReadAsync(conn, viewer, owner, "Europe/Stockholm", ct)).Days;

        // 23:30 UTC is the next day in Stockholm; days older than the calendar are left out.
        Assert.Equal([new(day.AddDays(-1), 1, 7000), new(day, 1, 6000), new(day.AddDays(1), 1, 5000)], await Days(owner));
        Assert.Equal([new(day.AddDays(-1), 1, 7000), new(day, 1, 6000)], await Days(follower));
        Assert.Equal([new CalendarDay(day.AddDays(-1), 1, 7000)], await Days(stranger));
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Http_summary_requires_sign_in_and_a_known_runner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var email = $"summary-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, email, "Owner", ct);
            var ownerId = (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.Id;
            var path = $"/api/runners/{ownerId}/summary?timeZone=Europe%2FStockholm";
            Assert.Empty((await owner.GetFromJsonAsync<RunnerSummary>(path, ct))!.Days);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/runners/{Guid.NewGuid()}/summary", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/runners/{ownerId}/summary?timeZone=Mars", ct)).StatusCode);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }
}
