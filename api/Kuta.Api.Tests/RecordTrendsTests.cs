using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class RecordTrendsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Trend_and_predictions_read_only_the_owners_efforts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            foreach (var id in new[] { userId, otherId })
                await conn.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
            async Task Effort(Guid user, string at, int distance, double elapsed, bool excluded = false) =>
                await conn.ExecuteAsync("""
                    with a as (
                        insert into activities (user_id, source, sport, started_at, duration_s, distance_m, exclude_from_records)
                        values (@user, 'manual', 'running', @at::timestamptz, 3600, 10000, @excluded) returning id
                    )
                    insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
                    select id, @user, @distance, @elapsed, 0, @at::timestamptz from a
                    """, new { user, at, distance, elapsed, excluded });

            // Import order differs from the activity dates.
            await Effort(userId, "2026-04-10T12:00:00Z", 5000, 1390);
            await Effort(userId, "2026-01-05T12:00:00Z", 5000, 1500);
            await Effort(userId, "2026-01-31T23:30:00Z", 5000, 1400); // februari i Stockholm
            await Effort(userId, "2026-02-10T12:00:00Z", 5000, 1450);
            await Effort(userId, "2026-03-10T12:00:00Z", 5000, 1400); // lika snabb: inget nytt rekord
            await Effort(userId, "2026-04-11T12:00:00Z", 10000, 2000);
            await Effort(otherId, "2026-01-01T12:00:00Z", 5000, 1000);

            await Effort(userId, "2026-04-12T12:00:00Z", 5000, 700, excluded: true);

            var trend = await RecordTrends.ReadAsync(conn, userId, 5000, "Europe/Stockholm", ct);
            Assert.Equal(new[] { (2026, 1, 1500d), (2026, 2, 1400d), (2026, 3, 1400d), (2026, 4, 1390d) },
                trend.Months.Select(m => (m.Year, m.Month, m.ElapsedS)));
            Assert.Equal(new[] { 1500d, 1400, 1390 }, trend.Progression.Select(e => e.ElapsedS));
            Assert.Equal(new DateTime(2026, 1, 31, 23, 30, 0, DateTimeKind.Utc), trend.Progression[1].AchievedAt);
            var efforts = (await conn.QueryAsync<StoredBestEffort>(
                "select activity_id, distance_m, elapsed_s, start_offset_s, achieved_at from best_efforts where user_id = @userId and distance_m = 5000 order by achieved_at",
                new { userId })).ToArray();
            var statuses = new List<string?>();
            foreach (var effort in efforts)
                statuses.Add(Assert.Single(await RecordTrends.ReadActivityAsync(conn, effort.ActivityId, userId)).RecordStatus);
            Assert.Equal(new string?[] { "previous", "previous", null, null, "current" }, statuses);
            Assert.Null(Assert.Single(await RecordTrends.ReadActivityAsync(conn, efforts[^1].ActivityId, otherId)).RecordStatus);

            // Deleting the latest record promotes its predecessor, retaining the older equal-time effort.
            await conn.ExecuteAsync("delete from activities where id = @id", new { id = efforts[^1].ActivityId });
            Assert.Equal("current", Assert.Single(await RecordTrends.ReadActivityAsync(conn, efforts[1].ActivityId, userId)).RecordStatus);
            Assert.Null(Assert.Single(await RecordTrends.ReadActivityAsync(conn, efforts[3].ActivityId, userId)).RecordStatus);
            await Effort(userId, "2026-04-10T12:00:00Z", 5000, 1390);
            var utc = await RecordTrends.ReadAsync(conn, userId, 5000, "UTC", ct);
            Assert.Equal((1, 1400d), (utc.Months[0].Month, utc.Months[0].ElapsedS));

            // Fönstret slutar vid "nu": januari ligger utanför 90 dagar från slutet av april.
            var now = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
            var prediction = await RecordTrends.PredictAsync(conn, userId, now, ct);
            Assert.Equal((10000, 2000d), (prediction.Basis!.DistanceM, prediction.Basis.ElapsedS));
            var april = await RecordTrends.PredictAsync(conn, userId, new DateTime(2026, 4, 10, 18, 0, 0, DateTimeKind.Utc), ct);
            Assert.Equal((5000, 1390d), (april.Basis!.DistanceM, april.Basis.ElapsedS));
            Assert.Null((await RecordTrends.PredictAsync(conn, userId, now.AddYears(1), ct)).Basis);

            var other = await RecordTrends.ReadAsync(conn, otherId, 5000, "UTC", ct);
            Assert.Equal(1000d, Assert.Single(other.Progression).ElapsedS);
            Assert.Empty((await RecordTrends.ReadAsync(conn, otherId, 10000, "UTC", ct)).Months);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@userId, @otherId)", new { userId, otherId });
        }
    }
}
