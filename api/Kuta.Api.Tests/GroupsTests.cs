using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class GroupsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    static async Task<Guid> CreateGroupAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid admin, string timeZone = "UTC") =>
        await conn.ExecuteScalarAsync<Guid>("""
            with g as (insert into groups (name, invite_code, time_zone) values ('Synthetic group', @code, @timeZone) returning id)
            insert into group_members (group_id, user_id, role, accepted_at) select id, @admin, 'admin', now() from g
            returning group_id
            """, new { admin, timeZone, code = ShareCodes.Generate() }, tx);

    static Task AddMemberAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid group, Guid user, bool accepted, bool share,
        DateTime? acceptedAt = null) =>
        conn.ExecuteAsync("""
            insert into group_members (group_id, user_id, accepted_at, share_totals)
            values (@group, @user, case when @accepted then coalesce(@acceptedAt, now()) end, @share)
            """, new { group, user, accepted, share, acceptedAt }, tx);

    [Fact]
    public async Task Leaderboard_counts_sharing_members_in_the_groups_week_without_Garmin_or_blockers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var (admin, sharing, silent, pending, outsider) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into auth.users (id) select unnest(@ids);
            insert into users (id, display_name) select id, 'Runner ' || n from unnest(@ids) with ordinality as t(id, n);
            insert into activities (user_id, source, sport, started_at, duration_s, distance_m) values
                -- Sunday 23:30 UTC is Monday 00:30 in Stockholm: inside the week. The next Sunday night is outside.
                (@sharing, 'manual', 'running', '2026-03-08T23:30:00Z', 1500, 5000),
                (@sharing, 'manual', 'running', '2026-03-15T23:30:00Z', 900, 3000),
                (@sharing, 'garmin', 'running', '2026-03-10T07:00:00Z', 3000, 10000),
                (@sharing, 'manual', 'cycling', '2026-03-11T07:00:00Z', 2400, 20000),
                (@admin, 'manual', 'trail run', '2026-03-12T07:00:00Z', 1200, 4000),
                (@silent, 'manual', 'running', '2026-03-12T07:00:00Z', 300, 1000),
                (@pending, 'manual', 'running', '2026-03-12T07:00:00Z', 300, 1000)
            """, new { ids = new[] { admin, sharing, silent, pending, outsider }, admin, sharing, silent, pending }, tx);
        var group = await CreateGroupAsync(conn, tx, admin, "Europe/Stockholm");
        await conn.ExecuteAsync("update group_members set share_totals = true where user_id = @admin", new { admin }, tx);
        await AddMemberAsync(conn, tx, group, sharing, accepted: true, share: true);
        await AddMemberAsync(conn, tx, group, silent, accepted: true, share: false);
        await AddMemberAsync(conn, tx, group, pending, accepted: false, share: true);
        var now = DateTimeOffset.Parse("2026-03-13T12:00:00Z");

        async Task<Dictionary<Guid, double>?> Board(Guid viewer, string? sportGroup, string metric, DateOnly? week = null) =>
            (await Groups.ReadLeaderboardAsync(conn, viewer, group, sportGroup, metric, week, now, ct))?.Rows
                .ToDictionary(r => r.UserId, r => r.Value);

        Assert.Equal(new() { [sharing] = 5000, [admin] = 4000 }, await Board(admin, "running", "distance"));
        // Garmin activities are left out, so the leaderboard needs no Garmin attribution yet.
        Assert.False((await Groups.ReadLeaderboardAsync(conn, admin, group, "running", "distance", null, now, ct))!.GarminData);
        Assert.Equal(new() { [sharing] = 2, [admin] = 1 }, await Board(silent, null, "count"));
        Assert.Equal(new() { [sharing] = 20000, [admin] = 0 }, await Board(admin, "cycling", "distance"));
        // Any day of the week gives the week from Monday.
        var board = await Groups.ReadLeaderboardAsync(conn, admin, group, "running", "distance", new DateOnly(2026, 3, 18), now, ct);
        Assert.Equal((new DateOnly(2026, 3, 16), 3000d), (board!.WeekStart, board.Rows[0].Value));

        // Neither outsiders nor members waiting for approval read the group.
        foreach (var viewer in new[] { outsider, pending })
        {
            Assert.Null(await Board(viewer, null, "count"));
            Assert.Null(await Groups.ReadAsync(conn, viewer, group, ct));
            Assert.Null(await Groups.ReadMembersAsync(conn, viewer, group, 0, ct));
        }
        var detail = (await Groups.ReadAsync(conn, admin, group, ct))!;
        Assert.Equal(3, detail.MemberCount);
        Assert.Equal(pending, Assert.Single(detail.Requests).Id);
        Assert.Empty((await Groups.ReadAsync(conn, silent, group, ct))!.Requests);

        // A member who blocked the viewer disappears from the viewer's group; the blocker still sees the blocked member.
        await conn.ExecuteAsync("insert into follow_blocks (blocker_id, blocked_id) values (@sharing, @admin)", new { sharing, admin }, tx);
        Assert.Equal(new[] { admin }, (await Board(admin, null, "count"))!.Keys);
        Assert.DoesNotContain((await Groups.ReadMembersAsync(conn, admin, group, 0, ct))!, m => m.Id == sharing);
        Assert.Equal(2, (await Groups.ReadAsync(conn, admin, group, ct))!.MemberCount);
        Assert.Contains((await Groups.ReadMembersAsync(conn, sharing, group, 0, ct))!, m => m.Id == admin);

        foreach (var table in new[] { "groups", "group_members" })
        {
            Assert.True(await conn.ExecuteScalarAsync<bool>("select relrowsecurity from pg_class where oid = @table::regclass", new { table }, tx));
            foreach (var role in new[] { "anon", "authenticated" })
                Assert.False(await conn.ExecuteScalarAsync<bool>("select has_table_privilege(@role, @table, 'SELECT,INSERT,UPDATE,DELETE')",
                    new { role, table }, tx));
        }
        await tx.RollbackAsync(ct);
    }

    // A club can have 1000 members, so the list comes a page at a time, admins first.
    [Fact]
    public async Task Group_goal_counts_the_leaderboards_rows_per_period_with_the_viewers_part()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var (admin, sharing, silent) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into auth.users (id) select unnest(@ids);
            insert into users (id, display_name) select id, 'Runner ' || n from unnest(@ids) with ordinality as t(id, n);
            insert into activities (user_id, source, sport, started_at, duration_s, distance_m) values
                -- Feb 28 23:30 UTC is March 1 in Stockholm: the current month.
                (@sharing, 'manual', 'running', '2026-02-28T23:30:00Z', 1500, 5000),
                (@sharing, 'manual', 'running', '2026-02-10T07:00:00Z', 1800, 6000),
                (@sharing, 'garmin', 'running', '2026-03-05T07:00:00Z', 3000, 10000),
                (@sharing, 'manual', 'cycling', '2026-03-06T07:00:00Z', 2400, 20000),
                (@admin, 'manual', 'running', '2026-03-03T07:00:00Z', 1200, 4000),
                (@silent, 'manual', 'running', '2026-03-04T07:00:00Z', 300, 1000),
                (@admin, 'manual', 'cycling', '2026-03-10T07:00:00Z', 600, 3000),
                (@sharing, 'manual', 'cycling', '2026-03-11T07:00:00Z', 600, 3000),
                (@sharing, 'garmin', 'cycling', '2026-03-12T07:00:00Z', 600, 3000)
            """, new { ids = new[] { admin, sharing, silent }, admin, sharing, silent }, tx);
        var group = await CreateGroupAsync(conn, tx, admin, "Europe/Stockholm");
        await conn.ExecuteAsync("update group_members set share_totals = true where user_id = @admin", new { admin }, tx);
        await AddMemberAsync(conn, tx, group, sharing, accepted: true, share: true);
        await AddMemberAsync(conn, tx, group, silent, accepted: true, share: false);
        var now = DateTimeOffset.Parse("2026-03-13T12:00:00Z");
        async Task<GroupGoal> Read(Guid viewer) => (await Groups.ReadGoalsAsync(conn, viewer, group, "Europe/Stockholm", now, ct))[0];

        Assert.Empty(await Groups.ReadGoalsAsync(conn, admin, group, "Europe/Stockholm", now, ct));
        await conn.ExecuteAsync("""
            insert into group_goals (group_id, sport_group, metric, period, target, starts_on, created_at)
            values (@group, 'running', 'distance', 'month', 9000, '2026-02-15', now()),
                (@group, null, 'count', 'week', 2, '2026-03-09', now() + interval '1 second')
            """, new { group }, tx);
        var goal = await Read(admin);
        // Each goal has its own sport, metric and period: the weekly count for all sports (from Monday March 9) has the
        // two cycling activities that week, not the Garmin one, and one of them is the viewer's.
        var weekly = (await Groups.ReadGoalsAsync(conn, admin, group, "Europe/Stockholm", now, ct))[1];
        Assert.Equal((new DateOnly(2026, 3, 9), 2d, 1d, true), (weekly.Progress.Current.Start, weekly.Progress.Current.Value,
            weekly.OwnValue, weekly.Progress.Current.Achieved));
        Assert.Equal((new DateOnly(2026, 3, 1), 9000d, true, 4000d), (goal.Progress.Current.Start, goal.Progress.Current.Value,
            goal.Progress.Current.Achieved, goal.OwnValue));
        Assert.Equal((6000d, false, 1), (goal.Progress.Previous!.Value, goal.Progress.Previous.Achieved, goal.Progress.Streak));
        Assert.False(goal.Progress.GarminData);
        // A member who doesn't share still sees the total but contributes nothing.
        Assert.Equal((9000d, 0d), ((await Read(silent)).Progress.Current.Value, (await Read(silent)).OwnValue));
        // Like the leaderboard, a member who blocked the viewer is left out of the viewer's total.
        await conn.ExecuteAsync("insert into follow_blocks (blocker_id, blocked_id) values (@sharing, @admin)", new { sharing, admin }, tx);
        Assert.Equal(4000d, (await Read(admin)).Progress.Current.Value);
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Members_are_paged_with_admins_first()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var ids = Enumerable.Range(0, Groups.MemberPageSize + 1).Select(_ => Guid.NewGuid()).ToArray();
        await conn.ExecuteAsync("""
            insert into auth.users (id) select unnest(@ids);
            insert into users (id, display_name) select id, 'A member' from unnest(@ids) as id
            """, new { ids }, tx);
        var admin = ids[^1];
        await conn.ExecuteAsync("update users set display_name = 'Zed admin' where id = @admin", new { admin }, tx);
        var group = await CreateGroupAsync(conn, tx, admin);
        foreach (var member in ids[..^1]) await AddMemberAsync(conn, tx, group, member, accepted: true, share: false);

        var first = (await Groups.ReadMembersAsync(conn, admin, group, 0, ct))!;
        Assert.Equal(Groups.MemberPageSize, first.Count);
        Assert.Equal(admin, first[0].Id);
        var rest = (await Groups.ReadMembersAsync(conn, admin, group, Groups.MemberPageSize, ct))!;
        Assert.Single(rest);
        Assert.Equal(ids.ToHashSet(), first.Concat(rest).Select(m => m.Id).ToHashSet());
        Assert.Equal(ids.Length, (await Groups.ReadAsync(conn, admin, group, ct))!.MemberCount);
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Last_admin_leaving_promotes_the_longest_member_and_an_empty_group_is_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var (admin, early, late, waiting) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into auth.users (id) select unnest(@ids);
            insert into users (id, display_name) select id, 'Runner' from unnest(@ids) as t(id);
            """, new { ids = new[] { admin, early, late, waiting } }, tx);
        var group = await CreateGroupAsync(conn, tx, admin);
        await AddMemberAsync(conn, tx, group, late, accepted: true, share: false, DateTime.UtcNow);
        await AddMemberAsync(conn, tx, group, early, accepted: true, share: false, DateTime.UtcNow.AddDays(-1));
        await AddMemberAsync(conn, tx, group, waiting, accepted: false, share: false);
        async Task<string?> Role(Guid user) => await conn.ExecuteScalarAsync<string?>(
            "select role from group_members where group_id = @group and user_id = @user", new { group, user }, tx);

        // Account deletion cascades through the same trigger.
        await conn.ExecuteAsync("delete from auth.users where id = @admin", new { admin }, tx);
        Assert.Equal(("admin", "member"), (await Role(early), await Role(late)));
        await conn.ExecuteAsync("delete from group_members where group_id = @group and user_id = any(@users)",
            new { group, users = new[] { early, late } }, tx);
        // Only a request is left, so the group is gone together with it.
        Assert.False(await conn.ExecuteScalarAsync<bool>("select exists(select 1 from groups where id = @group)", new { group }, tx));
        Assert.Null(await Role(waiting));
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Club_search_finds_open_clubs_by_name_skeleton_but_never_private_groups()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var (admin, viewer) = (Guid.NewGuid(), Guid.NewGuid());
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@admin), (@viewer);
            insert into users (id, display_name) values (@admin, 'Admin'), (@viewer, 'Viewer');
            """, new { admin, viewer }, tx);
        // A unique word keeps clubs from other tests out of the result.
        var word = $"Hässelby{Guid.NewGuid():N}"[..20];
        async Task<Guid> Group(string name, string visibility)
        {
            var id = await CreateGroupAsync(conn, tx, admin);
            await conn.ExecuteAsync("update groups set name = @name, name_skeleton = @skeleton, visibility = @visibility where id = @id",
                new { id, name, skeleton = MeEndpoints.NameSkeleton(name), visibility }, tx);
            return id;
        }
        var larger = await Group($"{word} löparklubb", "open");
        var exact = await Group(word.ToUpperInvariant(), "open");
        await Group($"{word} privat", "private");
        await AddMemberAsync(conn, tx, larger, viewer, accepted: true, share: false);

        var found = await Groups.SearchClubsAsync(conn, viewer, MeEndpoints.NameSkeleton(word.Replace('ä', 'a')), ct);
        Assert.Equal([exact, larger], found.Select(c => c.Id));
        Assert.Equal(("none", "member", 2), (found[0].Status, found[1].Status, found[1].MemberCount));
        // A longer name finds the clubs inside it, so that the web can warn before a duplicate is created.
        var longer = await Groups.SearchClubsAsync(conn, viewer, MeEndpoints.NameSkeleton($"{word} SK Friidrott"), ct);
        Assert.Equal([exact], longer.Select(c => c.Id));

        // An open club needs a skeleton to be found.
        await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
            "update groups set name_skeleton = null where id = @exact", new { exact }, tx));
        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task Invite_link_request_approval_and_sharing_over_http()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var admin = factory.CreateClient();
        using var joiner = factory.CreateClient();
        var emails = new[] { $"group-admin-{Guid.NewGuid():N}@example.com", $"group-joiner-{Guid.NewGuid():N}@example.com" };
        try
        {
            await TestSession.CreateRunnerAsync(factory, admin, conn, emails[0], "Admin", ct);
            await TestSession.CreateRunnerAsync(factory, joiner, conn, emails[1], "Joiner", ct);
            var joinerId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @email", new { email = emails[1] });

            using var invalid = await admin.PostAsJsonAsync("/api/groups", new GroupRequest("  "), ct);
            Assert.Equal("invalidGroupName", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            using var created = await admin.PostAsJsonAsync("/api/groups", new GroupRequest(" Morning club ", "Europe/Stockholm"), ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            var code = (await admin.GetFromJsonAsync<GroupDetail>($"/api/groups/{id}", ct))!.InviteCode;

            Assert.Equal(HttpStatusCode.NotFound, (await joiner.GetAsync($"/api/groups/{id}", ct)).StatusCode);
            var invite = await joiner.GetFromJsonAsync<GroupInvite>($"/api/group-invites/{code.ToUpperInvariant()}", ct);
            Assert.Equal(("Morning club", 1, "none"), (invite!.Name, invite.MemberCount, invite.Status));
            Assert.Equal(HttpStatusCode.OK, (await joiner.PostAsync($"/api/group-invites/{code}", null, ct)).StatusCode);
            Assert.Equal("pending", Assert.Single((await joiner.GetFromJsonAsync<GroupSummary[]>("/api/groups", ct))!).Status);
            // A request can neither share nor approve itself.
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.PutAsJsonAsync($"/api/groups/{id}/me", new ShareTotalsRequest(true), ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.PostAsync($"/api/groups/{id}/members/{joinerId}/accept", null, ct)).StatusCode);

            Assert.Equal(joinerId, Assert.Single((await admin.GetFromJsonAsync<GroupDetail>($"/api/groups/{id}", ct))!.Requests).Id);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/groups/{id}/members/{joinerId}/accept", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await joiner.PutAsJsonAsync($"/api/groups/{id}/me", new ShareTotalsRequest(true), ct)).StatusCode);
            var members = await joiner.GetFromJsonAsync<GroupMember[]>($"/api/groups/{id}/members", ct);
            Assert.Equal([("Admin", "admin"), ("Joiner", "member")], members!.Select(m => (m.DisplayName, m.Role)));
            var board = await joiner.GetFromJsonAsync<Leaderboard>($"/api/groups/{id}/leaderboard?sportGroup=running&metric=distance", ct);
            Assert.Equal(joinerId, Assert.Single(board!.Rows).UserId);
            Assert.Equal(HttpStatusCode.BadRequest, (await joiner.GetAsync($"/api/groups/{id}/leaderboard?metric=pace", ct)).StatusCode);

            // Shared goals are set by an admin and shown to every member, at most MaxGoals per group.
            var goal = new GoalRequest("running", "distance", "month", 100_000);
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.PostAsJsonAsync($"/api/groups/{id}/goals", goal, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/groups/{id}/goals", goal with { Metric = "pace" }, ct)).StatusCode);
            using var createdGoal = await admin.PostAsJsonAsync($"/api/groups/{id}/goals", goal, ct);
            Assert.Equal(HttpStatusCode.Created, createdGoal.StatusCode);
            var goalId = (await createdGoal.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            for (var i = 1; i < Groups.MaxGoals; i++)
                Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/groups/{id}/goals", goal with { Metric = "count", Target = i }, ct)).StatusCode);
            using var overLimit = await admin.PostAsJsonAsync($"/api/groups/{id}/goals", goal, ct);
            Assert.Equal("groupGoalLimit", (await overLimit.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.PutAsJsonAsync($"/api/groups/{id}/goals/{goalId}", goal, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/groups/{id}/goals/{goalId}", goal with { Target = 50_000 }, ct)).StatusCode);
            var goals = (await joiner.GetFromJsonAsync<GroupDetail>($"/api/groups/{id}", ct))!.Goals!;
            Assert.Equal((Groups.MaxGoals, goalId, 50_000d), (goals.Count, goals[0].Progress.Id, goals[0].Progress.Target));
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.DeleteAsync($"/api/groups/{id}/goals/{goalId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{id}/goals/{goalId}", ct)).StatusCode);
            Assert.Equal(Groups.MaxGoals - 1, (await joiner.GetFromJsonAsync<GroupDetail>($"/api/groups/{id}", ct))!.Goals!.Count);

            // Admin actions are not available to members.
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.PutAsJsonAsync($"/api/groups/{id}", new GroupRequest("Mine"), ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.PostAsync($"/api/groups/{id}/invite-code/replace", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.DeleteAsync($"/api/groups/{id}", ct)).StatusCode);

            var replaced = await (await admin.PostAsync($"/api/groups/{id}/invite-code/replace", null, ct)).Content.ReadFromJsonAsync<ShareCode>(ct);
            Assert.NotEqual(code, replaced!.Code);
            Assert.Equal(HttpStatusCode.NotFound, (await joiner.GetAsync($"/api/group-invites/{code}", ct)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await joiner.DeleteAsync($"/api/groups/{id}/members/{joinerId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{id}", ct)).StatusCode);
            Assert.Empty((await admin.GetFromJsonAsync<GroupSummary[]>("/api/groups", ct))!);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = any(@emails)", new { emails }); }
    }

    [Fact]
    public async Task Open_club_settings_search_page_and_request_over_http()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var admin = factory.CreateClient();
        using var visitor = factory.CreateClient();
        var emails = new[] { $"club-admin-{Guid.NewGuid():N}@example.com", $"club-visitor-{Guid.NewGuid():N}@example.com" };
        try
        {
            await TestSession.CreateRunnerAsync(factory, admin, conn, emails[0], "Admin", ct);
            await TestSession.CreateRunnerAsync(factory, visitor, conn, emails[1], "Visitor", ct);
            var name = $"Club{Guid.NewGuid():N}"[..16];
            // A club can be open from the start.
            using var invalidCreate = await admin.PostAsJsonAsync("/api/groups", new GroupRequest(name, Visibility: "public"), ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
            var openId = (await (await admin.PostAsJsonAsync("/api/groups",
                    new GroupRequest($"{name}open", Visibility: "open", Place: "Uppsala"), ct))
                .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            Assert.Equal("Uppsala", (await visitor.GetFromJsonAsync<ClubSummary>($"/api/clubs/{openId}", ct))!.Place);
            var id = (await (await admin.PostAsJsonAsync("/api/groups", new GroupRequest(name), ct))
                .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            // A private group is neither searchable nor open to requests by id.
            Assert.Equal([openId], (await visitor.GetFromJsonAsync<ClubSummary[]>($"/api/clubs?query={name}", ct))!.Select(c => c.Id));
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/clubs/{id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.PostAsync($"/api/clubs/{id}/join", null, ct)).StatusCode);

            using var invalid = await admin.PutAsJsonAsync($"/api/groups/{id}", new GroupRequest(name, Visibility: "public"), ct);
            Assert.Equal("invalidGroupSettings", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            var settings = new GroupRequest(name, Visibility: "open", Description: " Morning runs ", Place: "Stockholm", SportGroup: "running");
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.PutAsJsonAsync($"/api/groups/{id}", settings, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/groups/{id}", settings, ct)).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync("/api/clubs?query=ab", ct)).StatusCode);
            var club = (await visitor.GetFromJsonAsync<ClubSummary[]>($"/api/clubs?query={name.ToLowerInvariant()}", ct))!
                .Single(c => c.Id == id);
            Assert.Equal((id, "Stockholm", "Morning runs", 1, "none"), (club.Id, club.Place, club.Description, club.MemberCount, club.Status));
            // The club page shows the club, never its members.
            var page = await visitor.GetFromJsonAsync<JsonElement>($"/api/clubs/{id}", ct);
            Assert.False(page.TryGetProperty("members", out _));
            Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsync($"/api/clubs/{id}/join", null, ct)).StatusCode);
            Assert.Equal("pending", (await visitor.GetFromJsonAsync<ClubSummary>($"/api/clubs/{id}", ct))!.Status);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/groups/{id}", ct)).StatusCode);
            Assert.Equal("open", (await admin.GetFromJsonAsync<GroupDetail>($"/api/groups/{id}", ct))!.Visibility);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = any(@emails)", new { emails }); }
    }
}
