using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class TrainingLoadsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Daily_loads_use_stored_intensity_or_average_speed_for_the_owners_runs_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var now = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            foreach (var id in new[] { userId, otherId })
                await conn.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
            await conn.ExecuteAsync("update users set threshold_speed_m_s = 4 where id = @userId", new { userId });
            var manualRun = await conn.ExecuteScalarAsync<Guid>("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m, run_intensity) values
                (@userId, 'fit', 'running', '2026-06-30T06:00:00Z', 1800, 1000, 28800),
                (@userId, 'manual', 'cycling', '2026-06-30T07:00:00Z', 3600, 40000, null),
                (@userId, 'manual', 'running', '2026-07-01T06:00:00Z', 3600, 14400, null),
                (@otherId, 'manual', 'running', '2026-06-29T08:00:00Z', 3600, 14400, null);
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                values (@userId, 'manual', 'running', '2026-06-29T08:00:00Z', 3600, 14400) returning id
                """, new { userId, otherId });
            var triathlon = await conn.ExecuteScalarAsync<Guid>("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                values (@userId, 'fit', 'triathlon', '2026-06-27T22:30:00Z', 3600, 9000) returning id
                """, new { userId });
            await conn.ExecuteAsync("""
                insert into activity_parts (activity_id, original, part_index, sport, start_s, end_s, distance_m) values
                (@triathlon, true, 0, 'swimming', 0, 1800, 1800),
                (@triathlon, true, 1, 'running', 1800, 3600, 7200)
                """, new { triathlon });

            // Tröskelfart 4 m/s: en timme i 4 m/s ger 100. Triathlonet startar 00:30 lokal tid den 28 juni.
            var summary = await TrainingLoads.ReadAsync(conn, userId, "Europe/Stockholm", now, ct);
            Assert.Equal(("manual", 4d), (summary.ThresholdSource, summary.ThresholdSpeedMS!.Value));
            Assert.Null(summary.PredictedThresholdSpeedMS);
            Assert.Equal(new[] { (new DateOnly(2026, 6, 28), 50d), (new DateOnly(2026, 6, 29), 100d), (new DateOnly(2026, 6, 30), 50d) },
                summary.Days.Select(d => (d.Day, Math.Round(d.Load, 6))));
            Assert.Null(summary.RampPerWeek);
            Assert.False(summary.Warning);

            // Utan egen tröskelfart används prognosens VDOT; utan någon av dem finns ingen belastning.
            await conn.ExecuteAsync("""
                update users set threshold_speed_m_s = null where id = @userId;
                insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
                values (@manualRun, @userId, 10000, 2481, 0, '2026-06-29T08:00:00Z')
                """, new { userId, manualRun });
            var predicted = await TrainingLoads.ReadAsync(conn, userId, "Europe/Stockholm", now, ct);
            Assert.Equal("prediction", predicted.ThresholdSource);
            Assert.Equal(RacePredictor.ThresholdSpeed(RacePredictor.Vdot(10000, 2481)), predicted.ThresholdSpeedMS!.Value, precision: 9);
            Assert.Equal(predicted.ThresholdSpeedMS, predicted.PredictedThresholdSpeedMS);
            var none = await TrainingLoads.ReadAsync(conn, otherId, "UTC", now, ct);
            Assert.Null(none.ThresholdSpeedMS);
            Assert.Empty(none.Days);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@userId, @otherId)", new { userId, otherId });
        }
    }
}
