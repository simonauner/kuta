using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class YearReviewTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    static ReviewRow Row(DateTime local, double distanceM, double durationS, string group = "running", bool part = false,
        Guid? id = null, bool race = false, double elevationM = 0) =>
        new(id ?? Guid.NewGuid(), local, local, group, group, null, distanceM, durationS, elevationM, part, race);

    [Fact]
    public void Summary_counts_whole_activities_and_sport_parts_like_the_statistics()
    {
        var triathlon = Guid.NewGuid();
        var rows = new[]
        {
            Row(new DateTime(2026, 3, 2, 6, 30, 0), 10_000, 3000, elevationM: 50),
            Row(new DateTime(2026, 3, 3, 6, 45, 0), 21_100, 6300, race: true),
            Row(new DateTime(2026, 6, 6, 9, 0, 0), 51_500, 9000, "triathlon", id: triathlon),
            Row(new DateTime(2026, 6, 6, 9, 0, 0), 25_000, 3000, "running", part: true, id: triathlon),
        };
        var summary = YearReview.Summarize(rows);
        Assert.Equal(3, summary.Totals.Count);
        Assert.Equal(82_600, summary.Totals.DistanceM);
        Assert.Equal(50, summary.Totals.ElevationM);
        Assert.Equal(3, summary.Totals.ActiveDays);
        // The triathlon's running leg counts as running and is the longest run
        var running = summary.Sports.Single(s => s.SportGroup == "running");
        Assert.Equal((3, 56_100d), (running.Count, running.DistanceM));
        Assert.Equal(triathlon, summary.LongestRun!.ActivityId);
        Assert.Equal(triathlon, summary.LongestActivity!.ActivityId);
        Assert.Equal(2, summary.Months.Single(m => m.Month == 3).Count);
        Assert.Equal(21_100, Assert.Single(summary.Races).DistanceM);
        Assert.Equal(("morning", 3), (summary.TimeOfDay, summary.TimeOfDayCount));
    }

    [Fact]
    public void Streaks_count_consecutive_days_and_weeks()
    {
        // Monday–Wednesday, then Sunday the same week and Monday the week after
        var days = new[] { 2, 3, 4, 8, 9 }.Select(d => Row(new DateTime(2026, 3, d, 18, 0, 0), 5000, 1500)).ToList();
        days.Add(Row(new DateTime(2026, 3, 30, 18, 0, 0), 5000, 1500));
        var summary = YearReview.Summarize(days);
        Assert.Equal(3, summary.LongestDayStreak);
        Assert.Equal(2, summary.LongestWeekStreak);
        // Three Mondays: 2, 9 and 30 March
        Assert.Equal((1, 3), (summary.FavoriteWeekday, summary.FavoriteWeekdayCount));
        Assert.Empty(YearReview.Summarize([]).Sports);
        Assert.Null(YearReview.Summarize([]).FavoriteWeekday);
    }

    [Fact]
    public async Task Year_review_reads_own_activities_in_the_local_year_with_records_and_previous_year()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"review-a-{Guid.NewGuid():N}@example.com";
        var other = $"review-b-{Guid.NewGuid():N}@example.com";
        using var client = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await TestSession.CreateRunnerAsync(factory, client, conn, runner, "Reviewer", ct);
            await TestSession.CreateRunnerAsync(factory, otherClient, conn, other, "Other", ct);
            var userId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @runner", new { runner });
            // 23:30 UTC on New Year's Eve is already 2026 in Stockholm
            var newYear = await ActivityStorage.SaveManualAsync(conn, userId, new ManualActivityRequest("running", "New Year",
                new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero), 1500, 5000, null, null), ct);
            await ActivityStorage.SaveManualAsync(conn, userId, new ManualActivityRequest("running", "Last year",
                new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero), 1800, 5000, null, null), ct);
            var earlier = await ActivityStorage.SaveManualAsync(conn, userId, new ManualActivityRequest("running", "Old",
                new DateTimeOffset(2025, 5, 1, 8, 0, 0, TimeSpan.Zero), 1500, 5000, null, null), ct);
            await conn.ExecuteAsync("""
                insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
                values (@earlier, @userId, 5000, 1400, 0, '2025-05-01T08:00:00Z'),
                       (@newYear, @userId, 5000, 1450, 0, '2025-12-31T23:30:00Z')
                """, new { earlier = earlier!.Id, newYear = newYear!.Id, userId });

            var review = await client.GetFromJsonAsync<JsonElement>(
                "/api/activities/year-review?year=2026&timeZone=Europe/Stockholm", ct);
            Assert.Equal(DateTime.UtcNow.Year > 2026, review.GetProperty("complete").GetBoolean());
            Assert.Equal(1, review.GetProperty("summary").GetProperty("totals").GetProperty("count").GetInt32());
            Assert.Equal(2, review.GetProperty("previous").GetProperty("count").GetInt32());
            var record = Assert.Single(review.GetProperty("records").EnumerateArray());
            Assert.Equal(newYear!.Id, record.GetProperty("activityId").GetGuid());
            Assert.False(record.GetProperty("personalBest").GetBoolean());

            var others = await otherClient.GetFromJsonAsync<JsonElement>(
                "/api/activities/year-review?year=2026&timeZone=Europe/Stockholm", ct);
            Assert.Equal(0, others.GetProperty("summary").GetProperty("totals").GetProperty("count").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/activities/year-review?year=2026&timeZone=Mars/Base", ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner, @other)", new { runner, other });
        }
    }
}
