using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityStatisticsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Buckets_cover_local_period_and_weight_only_available_measurements_for_the_selected_owner_and_sport()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            foreach (var id in new[] { userId, otherId })
            {
                await conn.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
            }
            Task<ActivityStatisticsPeriod> Year(string timeZone, string? sportGroup) =>
                ActivityStatistics.ReadAsync(conn, userId, new(2026, 1, 1), new(2027, 1, 1), "month", timeZone, sportGroup, ct);
            var empty = await Year("UTC", null);
            Assert.Equal((0, 0d), (empty.Total.Count, empty.Total.DistanceM));
            Assert.All(empty.Buckets, m =>
            {
                Assert.Equal(0, m.Count);
                Assert.Null(m.PaceSPerKm);
                Assert.Null(m.AvgHr);
            });
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m, avg_hr) values
                (@userId, 'manual', 'running', '2025-12-31T23:30:00Z', 600, 2000, 120),
                (@userId, 'manual', 'running', '2026-01-20T12:00:00Z', 1800, 4000, 160),
                (@userId, 'manual', 'running', '2026-01-21T12:00:00Z', 600, 0, null),
                (@userId, 'manual', 'cycling', '2026-01-22T12:00:00Z', 3600, 30000, 180),
                (@otherId, 'manual', 'running', '2026-01-20T12:00:00Z', 9000, 90000, 200),
                (@userId, 'manual', 'running', '2026-12-31T23:30:00Z', 900, 3000, null),
                (@userId, 'manual', 'running', '2026-03-01T12:00:00Z', 900, 3000, null),
                (@userId, 'manual', 'trail run', '2026-03-02T12:00:00Z', 1200, 3000, null)
                """, new { userId, otherId });
            var year = await Year("Europe/Stockholm", "running");
            var months = year.Buckets;
            Assert.Equal(Enumerable.Range(1, 12).Select(m => new DateOnly(2026, m, 1)), months.Select(m => m.Start));
            Assert.Equal((5, 12000d), (year.Total.Count, year.Total.DistanceM));
            var january = months[0];
            Assert.Equal(3, january.Count);
            Assert.Equal(3000, january.DurationS);
            Assert.Equal(6000, january.DistanceM);
            Assert.Equal(400, january.PaceSPerKm);
            Assert.Equal(150, january.AvgHr);
            Assert.Equal(0, months[1].Count);
            Assert.Null(months[1].AvgHr);
            // The running group also counts trail running.
            Assert.Equal((2, 350d), (months[2].Count, months[2].PaceSPerKm));
            Assert.Null(months[2].AvgHr);
            Assert.Equal(0, months[11].Count);
            Assert.Equal(4, (await Year("Europe/Stockholm", null)).Buckets[0].Count);
            var utc = (await Year("UTC", "running")).Buckets;
            Assert.Equal(2, utc[0].Count);
            Assert.Equal(1, utc[11].Count);
            // ISO weeks start on Monday: a range starting mid-week opens with that week but only counts from the range start.
            var weeks = await ActivityStatistics.ReadAsync(conn, userId, new(2026, 1, 1), new(2026, 1, 26), "week", "Europe/Stockholm", "running", ct);
            Assert.Equal(new DateOnly[] { new(2025, 12, 29), new(2026, 1, 5), new(2026, 1, 12), new(2026, 1, 19) }, weeks.Buckets.Select(w => w.Start));
            Assert.Equal(new[] { 1, 0, 0, 2 }, weeks.Buckets.Select(w => w.Count));
            Assert.Equal(3, weeks.Total.Count);
            await conn.ExecuteAsync("update activities set avg_hr = null where user_id = @userId", new { userId });
            Assert.All((await Year("UTC", null)).Buckets, m => Assert.Null(m.AvgHr));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { userId, otherId } });
        }
    }
}
