using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityStatisticsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Months_cover_local_year_and_weight_only_available_measurements_for_the_selected_owner_and_sport()
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
            Assert.All(await ActivityStatistics.ReadAsync(conn, userId, 2026, "UTC", null, ct), m =>
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
                (@userId, 'manual', 'running', '2026-03-01T12:00:00Z', 900, 3000, null)
                """, new { userId, otherId });
            var months = await ActivityStatistics.ReadAsync(conn, userId, 2026, "Europe/Stockholm", "running", ct);
            Assert.Equal(Enumerable.Range(1, 12), months.Select(m => m.Month));
            var january = months[0];
            Assert.Equal(3, january.Count);
            Assert.Equal(3000, january.DurationS);
            Assert.Equal(6000, january.DistanceM);
            Assert.Equal(400, january.PaceSPerKm);
            Assert.Equal(150, january.AvgHr);
            Assert.Equal(0, months[1].Count);
            Assert.Null(months[1].AvgHr);
            Assert.Equal(300, months[2].PaceSPerKm);
            Assert.Null(months[2].AvgHr);
            Assert.Equal(0, months[11].Count);
            var all = await ActivityStatistics.ReadAsync(conn, userId, 2026, "Europe/Stockholm", null, ct);
            Assert.Equal(4, all[0].Count);
            var utc = await ActivityStatistics.ReadAsync(conn, userId, 2026, "UTC", "running", ct);
            Assert.Equal(2, utc[0].Count);
            Assert.Equal(1, utc[11].Count);
            await conn.ExecuteAsync("update activities set avg_hr = null where user_id = @userId", new { userId });
            Assert.All(await ActivityStatistics.ReadAsync(conn, userId, 2026, "UTC", null, ct), m => Assert.Null(m.AvgHr));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { userId, otherId } });
        }
    }
}
