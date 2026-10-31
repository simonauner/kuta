using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class GoalsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public void Validation_covers_kinds_sport_and_target_limits()
    {
        var valid = new GoalRequest(null, "distance", "week", 100_000_000);
        Assert.Null(Goals.Validate(valid));
        Assert.Null(Goals.Validate(valid with { SportGroup = "running", Metric = "count", Period = "year", Target = 10_000 }));
        Assert.Null(Goals.Validate(valid with { SportGroup = "cross-country skiing" }));
        Assert.Null(Goals.Validate(valid with { SportGroup = "weight training" }));
        Assert.Null(Goals.Validate(valid with { Metric = "duration", Period = "month", Target = 0.5 }));
        foreach (var invalid in new[] {
            valid with { SportGroup = "unknown" }, valid with { SportGroup = "trail run" }, valid with { Metric = null }, valid with { Metric = "pace" },
            valid with { Period = "day" }, valid with { Target = null }, valid with { Target = 0 },
            valid with { Target = double.NaN }, valid with { Target = 100_000_001 },
            valid with { Metric = "count", Target = 1.5 }, valid with { Metric = "count", Target = 10_001 } })
            Assert.Equal("invalidGoal", Goals.Validate(invalid)!.Code);
    }

    [Fact]
    public void Sport_groups_join_variants_and_every_activity_sport_belongs_to_exactly_one_selectable_group()
    {
        Assert.Equal("running", SportGroups.GroupOf("trail run"));
        Assert.Equal("cycling", SportGroups.GroupOf("gravel ride"));
        Assert.Equal("cross-country skiing", SportGroups.GroupOf("crosscountryskiing"));
        Assert.Equal("weight training", SportGroups.GroupOf("weight training"));
        foreach (var sport in ActivityInput.Sports)
            Assert.Single(SportGroups.All, g => g.Sports.Contains(sport));
        Assert.Equal(SportGroups.All.Length, SportGroups.All.Select(g => g.Key).Distinct().Count());
    }

    [Fact]
    public void Periods_start_on_monday_first_of_month_and_new_year()
    {
        Assert.Equal(new DateOnly(2026, 3, 9), Goals.PeriodStart("week", new DateOnly(2026, 3, 9)));
        Assert.Equal(new DateOnly(2026, 3, 9), Goals.PeriodStart("week", new DateOnly(2026, 3, 15)));
        Assert.Equal(new DateOnly(2026, 2, 1), Goals.PeriodStart("month", new DateOnly(2026, 2, 28)));
        Assert.Equal(new DateOnly(2026, 1, 1), Goals.PeriodStart("year", new DateOnly(2026, 12, 31)));
        Assert.Equal(new DateOnly(2026, 3, 1), Goals.NextPeriod("month", new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Recurring_goals_report_current_previous_and_consecutive_achieved_periods()
    {
        var goal = new StoredGoal(Guid.NewGuid(), "running", "distance", "month", 200_000, true, new DateOnly(2026, 1, 20));
        var values = new Dictionary<DateOnly, double>
        {
            [new(2026, 1, 1)] = 250_000, [new(2026, 2, 1)] = 199_999, [new(2026, 3, 1)] = 200_000, [new(2026, 4, 1)] = 50_000,
        };
        var april = Goals.Evaluate(goal, new DateOnly(2026, 4, 2), values);
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), 50_000d, false),
            (april.Current.Start, april.Current.End, april.Current.Value, april.Current.Achieved));
        Assert.Equal((new DateOnly(2026, 3, 1), true), (april.Previous!.Start, april.Previous.Achieved));
        Assert.Equal(1, april.Streak);
        Assert.False(april.Ended);

        // Ett uppnått pågående mål räknas in; en missad period bryter raden.
        values[new(2026, 4, 1)] = 200_000;
        Assert.Equal(2, Goals.Evaluate(goal, new DateOnly(2026, 4, 2), values).Streak);
        var march = Goals.Evaluate(goal, new DateOnly(2026, 3, 5), values);
        Assert.Equal((false, 1), (march.Previous!.Achieved, march.Streak));

        // Perioder före startperioden räknas inte; ett "idag" före start (annan tidszon) visar första perioden.
        values[new(2025, 12, 1)] = 300_000;
        var first = Goals.Evaluate(goal, new DateOnly(2026, 1, 25), values);
        Assert.Null(first.Previous);
        Assert.Equal(1, first.Streak);
        Assert.Equal(new DateOnly(2026, 1, 1), Goals.Evaluate(goal, new DateOnly(2025, 12, 31), values).Current.Start);
    }

    [Fact]
    public void One_time_goals_keep_their_period_and_end_afterwards()
    {
        var goal = new StoredGoal(Guid.NewGuid(), null, "count", "year", 100, false, new DateOnly(2026, 5, 1));
        var values = new Dictionary<DateOnly, double> { [new(2026, 1, 1)] = 100, [new(2027, 1, 1)] = 3 };
        var during = Goals.Evaluate(goal, new DateOnly(2026, 10, 31), values);
        Assert.Equal((new DateOnly(2026, 1, 1), 100d, true, false), (during.Current.Start, during.Current.Value, during.Current.Achieved, during.Ended));
        var after = Goals.Evaluate(goal, new DateOnly(2027, 1, 2), values);
        Assert.Equal((new DateOnly(2026, 1, 1), true), (after.Current.Start, after.Ended));
        Assert.Null(after.Previous);
        Assert.Equal(0, after.Streak);
    }

    [Fact]
    public async Task Progress_sums_owner_activities_per_local_period_and_storage_is_owner_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (owner, other) = (ids[0], ids[1]);
        try
        {
            foreach (var id in ids)
                await conn.ExecuteAsync("""
                    insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')
                    """, new { id });
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m) values
                (@owner, 'manual', 'running', '2026-02-17T12:00:00Z', 1500, 4000),
                (@owner, 'manual', 'running', '2026-02-24T12:00:00Z', 3600, 10000),
                (@owner, 'manual', 'running', '2026-03-03T12:00:00Z', 3600, 10000),
                (@owner, 'manual', 'running', '2026-03-08T23:30:00Z', 1200, 3000),
                (@owner, 'manual', 'running', '2026-03-09T10:00:00Z', 1800, 5000),
                (@owner, 'manual', 'cycling', '2026-03-10T10:00:00Z', 2400, 20000),
                (@owner, 'manual', 'trail run', '2026-03-10T16:00:00Z', 300, 500),
                (@other, 'manual', 'running', '2026-03-09T10:00:00Z', 9000, 50000);
                with triathlon as (
                    insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                    values (@owner, 'manual', 'triathlon', '2026-03-10T06:00:00Z', 1800, 4750) returning id
                )
                insert into activity_parts (activity_id, original, part_index, sport, start_s, end_s, distance_m)
                select id, true, 0, 'swimming', 0, 600, 750 from triathlon
                union all select id, true, 1, 'running', 600, 1800, 4000 from triathlon
                """, new { owner, other });
            var created = new DateTimeOffset(2026, 2, 17, 12, 0, 0, TimeSpan.Zero);
            var now = new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero);
            var running = await Goals.CreateAsync(conn, owner, new GoalRequest("running", "distance", "week", 10_000, true), "Europe/Stockholm", created, ct);
            var sessions = await Goals.CreateAsync(conn, owner, new GoalRequest(null, "count", "week", 4, true), "Europe/Stockholm", now, ct);
            var oneTime = await Goals.CreateAsync(conn, owner, new GoalRequest("cycling", "distance", "month", 100_000, false),
                "UTC", new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), ct);

            var stockholm = (await Goals.LoadAsync(conn, owner, "Europe/Stockholm", now, ct)).ToDictionary(g => g.Id);
            Assert.Equal(new DateOnly(2026, 2, 17), stockholm[running].StartsOn);
            // 5 + 3 (måndag 00:30 lokal tid) + 0,5 terräng + 4 km löpdel av triathlon; cykling och andra användare räknas inte.
            Assert.Equal((new DateOnly(2026, 3, 9), 12_500d, true), (stockholm[running].Current.Start, stockholm[running].Current.Value, stockholm[running].Current.Achieved));
            Assert.Equal(10_000, stockholm[running].Previous!.Value);
            Assert.Equal(3, stockholm[running].Streak);
            // Utan sport räknas triathlonpasset en gång.
            Assert.Equal(5, stockholm[sessions].Current.Value);
            // Skapat på onsdagen: veckans tidigare pass räknas, men inte förra veckan.
            Assert.Null(stockholm[sessions].Previous);
            Assert.Equal(1, stockholm[sessions].Streak);
            Assert.Equal((true, 0d), (stockholm[oneTime].Ended, stockholm[oneTime].Current.Value));

            var utc = (await Goals.LoadAsync(conn, owner, "UTC", now, ct)).ToDictionary(g => g.Id);
            Assert.Equal((9_500d, 13_000d, 2), (utc[running].Current.Value, utc[running].Previous!.Value, utc[running].Streak));

            Assert.Empty(await Goals.LoadAsync(conn, other, "UTC", now, ct));
            var update = new GoalRequest("running", "duration", "week", 4200, true);
            Assert.False(await Goals.UpdateAsync(conn, other, running, update, ct));
            Assert.False(await Goals.DeleteAsync(conn, other, running, ct));
            Assert.True(await Goals.UpdateAsync(conn, owner, running, update, ct));
            var duration = (await Goals.LoadAsync(conn, owner, "Europe/Stockholm", now, ct)).Single(g => g.Id == running);
            Assert.Equal((4500d, true, new DateOnly(2026, 2, 17)), (duration.Current.Value, duration.Current.Achieved, duration.StartsOn));
            Assert.True(await Goals.DeleteAsync(conn, owner, sessions, ct));

            var invalid = await Assert.ThrowsAsync<PostgresException>(() =>
                Goals.CreateAsync(conn, owner, update with { Target = 0 }, "UTC", now, ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
            Assert.True(await conn.ExecuteScalarAsync<bool>("select relrowsecurity from pg_class where oid = 'goals'::regclass"));
            foreach (var role in new[] { "anon", "authenticated" })
                Assert.False(await conn.ExecuteScalarAsync<bool>("select has_table_privilege(@role, 'goals', 'SELECT,INSERT,UPDATE,DELETE')", new { role }));
            await conn.ExecuteAsync("delete from auth.users where id = @owner", new { owner });
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("select count(*) from goals where user_id = @owner", new { owner }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids }); }
    }

    [Fact]
    public async Task Http_crud_uses_session_owner_and_never_exposes_another_users_goal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var emails = new[] { $"goal-owner-{Guid.NewGuid():N}@example.com", $"goal-other-{Guid.NewGuid():N}@example.com" };
        const string path = "/api/goals";
        const string list = "/api/goals?timeZone=Europe%2FStockholm";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, emails[0], "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, other, conn, emails[1], "Other", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
            var groups = await owner.GetFromJsonAsync<SportGroup[]>("/api/activities/sport-groups", ct);
            Assert.Contains("trail run", groups!.Single(g => g.Key == "running").Sports);
            var request = new GoalRequest("running", "distance", "week", 10_000, true, "Europe/Stockholm");
            using var created = await owner.PostAsJsonAsync(path, request, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

            var goal = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(list, ct)).EnumerateArray());
            Assert.Equal(id, goal.GetProperty("id").GetGuid());
            Assert.Equal(("running", "distance"), (goal.GetProperty("sportGroup").GetString(), goal.GetProperty("metric").GetString()));
            Assert.Equal(0, goal.GetProperty("current").GetProperty("value").GetDouble());
            Assert.False(goal.GetProperty("current").GetProperty("achieved").GetBoolean());
            Assert.Equal(JsonValueKind.Null, goal.GetProperty("previous").ValueKind);
            Assert.Equal(0, goal.GetProperty("streak").GetInt32());

            Assert.Empty((await other.GetFromJsonAsync<JsonElement>(list, ct)).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"{path}/{id}", request, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{path}/{id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{path}/{id}", request with { Period = "month" }, ct)).StatusCode);
            Assert.Equal("month", Assert.Single((await owner.GetFromJsonAsync<JsonElement>(list, ct)).EnumerateArray()).GetProperty("period").GetString());

            using var invalid = await owner.PostAsJsonAsync(path, request with { Target = 0 }, ct);
            Assert.Equal("invalidGoal", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, request with { TimeZone = "Mars/Base" }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{path}/{id}", ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(list, ct)).EnumerateArray());
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = any(@emails)", new { emails }); }
    }
}
