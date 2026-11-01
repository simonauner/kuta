using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class DailyTotalsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Days_follow_local_time_sport_groups_and_multisport_parts_for_the_owner_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            foreach (var id in new[] { userId, otherId })
                await conn.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
            var triathlon = await conn.ExecuteScalarAsync<Guid>("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m) values
                (@userId, 'manual', 'running', '2025-12-31T23:30:00Z', 1800, 5000),
                (@userId, 'manual', 'trail run', '2026-01-01T10:00:00Z', 3600, 8000),
                (@userId, 'manual', 'cycling', '2026-01-01T12:00:00Z', 3600, 30000),
                (@otherId, 'manual', 'running', '2026-01-01T10:00:00Z', 9000, 90000);
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                values (@userId, 'fit', 'triathlon', '2026-03-01T08:00:00Z', 7000, 51000) returning id
                """, new { userId, otherId });
            await conn.ExecuteAsync("""
                insert into activity_parts (activity_id, original, part_index, sport, start_s, end_s, distance_m) values
                (@triathlon, true, 0, 'swimming', 0, 1500, 1000),
                (@triathlon, true, 1, 'cycling', 1500, 5000, 40000),
                (@triathlon, true, 2, 'running', 5000, 7000, 10000)
                """, new { triathlon });

            var running = await DailyTotals.ReadAsync(conn, userId, "Europe/Stockholm", "running", ct);
            // The New Year's Eve activity starts 00:30 local time; trail running belongs to the running group; the triathlon's run part counts.
            Assert.Equal(new[] { (new DateOnly(2026, 1, 1), 2, 13000d, 5400d), (new DateOnly(2026, 3, 1), 1, 10000d, 2000d) },
                running.Select(d => (d.Day, d.Count, d.DistanceM, d.DurationS)));
            var utc = await DailyTotals.ReadAsync(conn, userId, "UTC", "running", ct);
            Assert.Equal(new DateOnly(2025, 12, 31), utc[0].Day);

            // Without a group the triathlon counts once with its full distance.
            var all = await DailyTotals.ReadAsync(conn, userId, "Europe/Stockholm", null, ct);
            Assert.Equal(new[] { (new DateOnly(2026, 1, 1), 3, 43000d), (new DateOnly(2026, 3, 1), 1, 51000d) },
                all.Select(d => (d.Day, d.Count, d.DistanceM)));

            Assert.Equal(90000d, Assert.Single(await DailyTotals.ReadAsync(conn, otherId, "UTC", null, ct)).DistanceM);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@userId, @otherId)", new { userId, otherId });
        }
    }
}
