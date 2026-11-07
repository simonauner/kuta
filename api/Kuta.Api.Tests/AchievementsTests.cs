using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class AchievementsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    static int id;

    // Local time = UTC in these rows
    static AchievementRow Row(DateTime at, double distanceM, string sport = "running", double elevationM = 0, bool part = false,
        Guid? activity = null) =>
        new(activity ?? new Guid(++id, 0, 0, new byte[8]), at, at, sport, distanceM, elevationM, part);

    static Achievement[] Kind(IEnumerable<Achievement> achievements, string kind) =>
        achievements.Where(a => a.Kind == kind).ToArray();

    [Fact]
    public void Running_distance_milestones_belong_to_the_activity_that_crosses_them()
    {
        var day = new DateTime(2026, 1, 5, 7, 0, 0);
        var rows = Enumerable.Range(0, 12).Select(i => Row(day.AddDays(i * 7), 45_000)).ToList();
        // A bike ride never counts as running
        rows.Add(Row(day.AddDays(1), 500_000, "cycling"));
        var result = Achievements.Evaluate(rows, []);

        var lifetime = Kind(result, "runDistance");
        Assert.Equal([470_000d, 100_000d], lifetime.Select(a => a.Threshold));
        Assert.Equal(rows[2].Id, lifetime.Single(a => a.Threshold == 100_000).ActivityId);
        Assert.Equal(rows[10].Id, lifetime.Single(a => a.Threshold == 470_000).ActivityId);
        Assert.Equal(rows[0].Id, Assert.Single(Kind(result, "firstUltra")).ActivityId);
        // 4 × 45 km in January (> 100 km) is the first 100 km month; the 200 km month needs 5 runs, which no month has
        var month = Assert.Single(Kind(result, "monthRunDistance"));
        Assert.Equal((100_000d, rows[2].Id), (month.Threshold, month.ActivityId));
        Assert.Equal(500_000d, Assert.Single(Kind(result, "yearRunDistance")).Threshold);
    }

    [Fact]
    public void Multisport_running_parts_count_as_running_but_not_as_extra_activities()
    {
        var at = new DateTime(2026, 6, 1, 8, 0, 0);
        var activity = Guid.NewGuid();
        var rows = new[]
        {
            Row(at, 150_000, "triathlon", activity: activity),
            Row(at, 110_000, "running", part: true, activity: activity),
        };
        var result = Achievements.Evaluate(rows, []);
        Assert.Equal(activity, Assert.Single(Kind(result, "runDistance")).ActivityId);
        Assert.Empty(Kind(result, "activityCount"));
    }

    [Fact]
    public void Elevation_counts_per_calendar_year_and_streaks_need_consecutive_weeks()
    {
        var rows = new List<AchievementRow>
        {
            Row(new DateTime(2025, 12, 30, 12, 0, 0), 10_000, "hiking", 2_000),
            Row(new DateTime(2026, 1, 2, 12, 0, 0), 10_000, "hiking", 2_000),
        };
        Assert.Empty(Kind(Achievements.Evaluate(rows, []), "climb"));
        rows.Add(Row(new DateTime(2026, 1, 9, 12, 0, 0), 10_000, "hiking", 200));
        var climb = Assert.Single(Kind(Achievements.Evaluate(rows, []), "climb"));
        Assert.Equal((2_097d, 2026), (climb.Threshold, climb.Year));

        // Tuesday 30 Dec and Friday 2 Jan are the same week; 9 Jan is the next, 23 Jan breaks the streak.
        var start = new DateTime(2026, 1, 23, 12, 0, 0);
        rows.AddRange(Enumerable.Range(0, 4).Select(w => Row(start.AddDays(7 * w), 5_000)));
        var streak = Assert.Single(Kind(Achievements.Evaluate(rows, []), "weekStreak"));
        Assert.Equal(rows[^1].Id, streak.ActivityId);
    }

    [Fact]
    public void First_distances_come_from_the_best_efforts_and_time_of_day_from_the_local_start()
    {
        var first5k = new FirstEffort(5000, new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc), Guid.NewGuid());
        var rows = new[]
        {
            Row(new DateTime(2026, 3, 2, 5, 30, 0), 5_000),
            Row(new DateTime(2026, 3, 3, 23, 0, 0), 5_000),
            Row(new DateTime(2026, 3, 4, 2, 0, 0), 5_000),
        };
        var result = Achievements.Evaluate(rows, [first5k, new FirstEffort(1000, first5k.AchievedAt, first5k.ActivityId)]);
        Assert.Equal(first5k.ActivityId, Assert.Single(Kind(result, "firstDistance")).ActivityId);
        Assert.Equal(rows[0].Id, Assert.Single(Kind(result, "earlyBird")).ActivityId);
        Assert.Equal(rows[1].Id, Assert.Single(Kind(result, "nightOwl")).ActivityId);
    }

    [Fact]
    public async Task Achievements_are_read_per_user_and_only_earned_keys_can_be_marked_seen()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"achieve-a-{Guid.NewGuid():N}@example.com";
        var other = $"achieve-b-{Guid.NewGuid():N}@example.com";
        using var client = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await TestSession.CreateRunnerAsync(factory, client, conn, runner, "Achiever", ct);
            await TestSession.CreateRunnerAsync(factory, otherClient, conn, other, "Other", ct);
            var userId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @runner", new { runner });
            // 04:30 in Stockholm (UTC+1 in January): early bird there, not in UTC
            await ActivityStorage.SaveManualAsync(conn, userId, new ManualActivityRequest("running", "Early",
                new DateTimeOffset(2026, 1, 10, 3, 30, 0, TimeSpan.Zero), 6000, 101_000, null, null), ct);

            var list = await client.GetFromJsonAsync<JsonElement>("/api/achievements?timeZone=Europe/Stockholm", ct);
            var keys = list.EnumerateArray().Select(a => a.GetProperty("key").GetString()).ToArray();
            Assert.Contains("early-bird", keys);
            Assert.Contains("run-distance-100000", keys);
            Assert.All(list.EnumerateArray(), a => Assert.False(a.GetProperty("seen").GetBoolean()));
            Assert.Empty(await otherClient.GetFromJsonAsync<JsonElement[]>("/api/achievements?timeZone=Europe/Stockholm", ct) ?? []);

            var seen = await client.PostAsJsonAsync("/api/achievements/seen",
                new { keys = new[] { "early-bird", "invented-key" }, timeZone = "Europe/Stockholm" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
            Assert.Equal(["early-bird"], await conn.QueryAsync<string>(
                "select key from achievement_views where user_id = @userId", new { userId }));
            var after = await client.GetFromJsonAsync<JsonElement>("/api/achievements?timeZone=Europe/Stockholm", ct);
            Assert.Equal(["early-bird"], after.EnumerateArray().Where(a => a.GetProperty("seen").GetBoolean())
                .Select(a => a.GetProperty("key").GetString()));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner, @other)", new { runner, other });
        }
    }
}
