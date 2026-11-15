using System.Security.Claims;
using Dapper;
using Npgsql;

public record GroupRequest(string? Name, string? TimeZone = null);
public record ShareTotalsRequest(bool ShareTotals);
// Status: "member" or "pending" (waiting for an admin).
public record GroupSummary(Guid Id, string Name, string Role, string Status, int MemberCount);
// JoinedAt and SimilarTo are set only for requests, as for follow requests: they help an admin spot impersonation.
public record GroupMember(Guid Id, string DisplayName, string Role, Guid? AvatarId, string? PreviousName,
    DateTime? JoinedAt = null, string? SimilarTo = null);
public record GroupDetail(Guid Id, string Name, string TimeZone, string InviteCode, string Role, bool ShareTotals,
    IReadOnlyList<GroupMember> Members, IReadOnlyList<GroupMember> Requests);
// Status for the visitor: "none", "pending" or "member".
public record GroupInvite(Guid Id, string Name, int MemberCount, string Status);
public record LeaderboardRow(Guid UserId, string DisplayName, Guid? AvatarId, double Value);
public record Leaderboard(DateOnly WeekStart, IReadOnlyList<LeaderboardRow> Rows);

// Invite-only groups. Membership never grants access to activities: members see each other and a weekly leaderboard of
// the totals each member chose to share (all own activities, also private ones, but never Garmin activities, which may
// not be shown to others before Garmin approves it).
public static class Groups
{
    public const int MaxMembers = 100;
    public const int MaxGroupsPerUser = 20;

    static readonly ApiError InvalidName = new("Ange ett namn på högst 100 tecken.", "invalidGroupName");
    static readonly ApiError GroupLimit = new($"Du kan vara med i högst {MaxGroupsPerUser} grupper.", "groupLimit");
    static readonly ApiError GroupFull = new($"Gruppen har redan {MaxMembers} medlemmar.", "groupFull");
    static readonly ApiError NameRequired = new("Välj ett namn först.", "nameRequired");
    static readonly ApiError InvalidLeaderboard = new("Kontrollera sport, typ och vecka.", "invalidLeaderboard");
    static readonly ApiError InvalidTimeZone = new("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone");

    public static string? NormalizeName(string? name) => name?.Trim() is { Length: >= 1 and <= 100 } trimmed ? trimmed : null;

    // A group where the viewer is a member (accepted) and that member's role.
    const string Membership = """
        select role from group_members where group_id = @id and user_id = @userId and accepted_at is not null
        """;

    public static void MapGroups(this RouteGroupBuilder api)
    {
        var groups = api.MapGroup("/groups").RequireAuthorization();
        groups.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return Results.Ok(await conn.QueryAsync<GroupSummary>(new CommandDefinition("""
                select g.id, g.name, m.role, case when m.accepted_at is null then 'pending' else 'member' end as status,
                    (select count(*) from group_members c where c.group_id = g.id and c.accepted_at is not null)::int as member_count
                from group_members m join groups g on g.id = m.group_id
                where m.user_id = @userId
                order by m.accepted_at is null, g.name, g.id
                """, new { userId = user.GetUserId() }, cancellationToken: ct)));
        });
        groups.MapPost("", async (GroupRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (NormalizeName(req.Name) is not { } name) return Results.BadRequest(InvalidName);
            var timeZone = req.TimeZone ?? "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct)) return Results.BadRequest(InvalidTimeZone);
            var userId = user.GetUserId();
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (await LockMemberAsync(conn, tx, userId, ct) is { } error) return error;
            var id = await WithNewCodeAsync(code => conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                with g as (insert into groups (name, invite_code, time_zone) values (@name, @code, @timeZone) returning id)
                insert into group_members (group_id, user_id, role, accepted_at) select id, @userId, 'admin', now() from g
                returning group_id
                """, new { name, code, timeZone, userId }, tx, cancellationToken: ct)), conn, tx, ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/groups/{id}", new { id });
        });
        groups.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var detail = await ReadAsync(conn, user.GetUserId(), id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        groups.MapPut("/{id:guid}", async (Guid id, GroupRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (NormalizeName(req.Name) is not { } name) return Results.BadRequest(InvalidName);
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition($"""
                update groups set name = @name where id = @id and ({Membership}) = 'admin'
                """, new { id, name, userId = user.GetUserId() }, cancellationToken: ct)) > 0 ? Results.NoContent() : Results.NotFound();
        });
        groups.MapDelete("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition($"""
                delete from groups where id = @id and ({Membership}) = 'admin'
                """, new { id, userId = user.GetUserId() }, cancellationToken: ct)) > 0 ? Results.NoContent() : Results.NotFound();
        });
        groups.MapPost("/{id:guid}/invite-code/replace", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var code = await WithNewCodeAsync(code => conn.ExecuteScalarAsync<string?>(new CommandDefinition($"""
                update groups set invite_code = @code where id = @id and ({Membership}) = 'admin' returning invite_code
                """, new { id, code, userId = user.GetUserId() }, tx, cancellationToken: ct)), conn, tx, ct);
            await tx.CommitAsync(ct);
            return code is null ? Results.NotFound() : Results.Ok(new ShareCode(code));
        });
        groups.MapPut("/{id:guid}/me", async (Guid id, ShareTotalsRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition("""
                update group_members set share_totals = @ShareTotals
                where group_id = @id and user_id = @userId and accepted_at is not null
                """, new { id, req.ShareTotals, userId = user.GetUserId() }, cancellationToken: ct)) > 0
                ? Results.NoContent() : Results.NotFound();
        });
        groups.MapPost("/{id:guid}/members/{memberId:guid}/accept", async (Guid id, Guid memberId, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            // The group row lock keeps concurrent approvals under the member limit.
            if (await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition($"""
                    select ({Membership}) from groups where id = @id for update
                    """, new { id, userId = user.GetUserId() }, tx, cancellationToken: ct)) != "admin")
                return Results.NotFound();
            if (await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "select count(*) from group_members where group_id = @id and accepted_at is not null",
                    new { id }, tx, cancellationToken: ct)) >= MaxMembers)
                return Results.Conflict(GroupFull);
            var updated = await conn.ExecuteAsync(new CommandDefinition("""
                update group_members set accepted_at = coalesce(accepted_at, now()) where group_id = @id and user_id = @memberId
                """, new { id, memberId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return updated > 0 ? Results.NoContent() : Results.NotFound();
        });
        groups.MapPost("/{id:guid}/members/{memberId:guid}/admin", async (Guid id, Guid memberId, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition($"""
                update group_members set role = 'admin'
                where group_id = @id and user_id = @memberId and accepted_at is not null and ({Membership}) = 'admin'
                """, new { id, memberId, userId = user.GetUserId() }, cancellationToken: ct)) > 0 ? Results.NoContent() : Results.NotFound();
        });
        // Leaving (also withdrawing one's own request), or an admin removing a member or declining a request.
        groups.MapDelete("/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var userId = user.GetUserId();
            return await conn.ExecuteAsync(new CommandDefinition($"""
                delete from group_members where group_id = @id and user_id = @memberId
                    and (@memberId = @userId or ({Membership}) = 'admin')
                """, new { id, memberId, userId }, cancellationToken: ct)) > 0 ? Results.NoContent() : Results.NotFound();
        });
        groups.MapGet("/{id:guid}/leaderboard", async (Guid id, string? sportGroup, string? metric, DateOnly? week,
            NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            metric ??= "distance";
            if ((sportGroup is not null && !SportGroups.IsValid(sportGroup)) || !Goals.Metrics.Contains(metric))
                return Results.BadRequest(InvalidLeaderboard);
            await using var conn = await db.OpenConnectionAsync(ct);
            var board = await ReadLeaderboardAsync(conn, user.GetUserId(), id, sportGroup, metric, week, DateTimeOffset.UtcNow, ct);
            return board is null ? Results.NotFound() : Results.Ok(board);
        });

        // An invite link only reveals the group's name and size. Rate limited so that codes cannot be enumerated.
        var invites = api.MapGroup("/group-invites/{code}").RequireAuthorization();
        invites.MapGet("", async (string code, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var invite = await conn.QuerySingleOrDefaultAsync<GroupInvite>(new CommandDefinition("""
                select g.id, g.name,
                    (select count(*) from group_members c where c.group_id = g.id and c.accepted_at is not null)::int as member_count,
                    case when m.user_id is null then 'none' when m.accepted_at is null then 'pending' else 'member' end as status
                from groups g left join group_members m on m.group_id = g.id and m.user_id = @userId
                where g.invite_code = @code
                """, new { code = code.ToLowerInvariant(), userId = user.GetUserId() }, cancellationToken: ct));
            return invite is null ? Results.NotFound() : Results.Ok(invite);
        }).RequireRateLimiting("shareCodes");
        invites.MapPost("", async (string code, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var userId = user.GetUserId();
            await using var tx = await conn.BeginTransactionAsync(ct);
            var id = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select id from groups where invite_code = @code", new { code = code.ToLowerInvariant() }, tx, cancellationToken: ct));
            if (id is null) return Results.NotFound();
            if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from group_members where group_id = @id and user_id = @userId)",
                    new { id, userId }, tx, cancellationToken: ct)))
            {
                if (await LockMemberAsync(conn, tx, userId, ct) is { } error) return error;
                await conn.ExecuteAsync(new CommandDefinition("""
                    insert into group_members (group_id, user_id) values (@id, @userId) on conflict do nothing
                    """, new { id, userId }, tx, cancellationToken: ct));
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { id });
        }).RequireRateLimiting("follows");
    }

    // Locks the profile so that parallel joins stay under the group limit; requires a chosen name.
    static async Task<IResult?> LockMemberAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid userId, CancellationToken ct)
    {
        var name = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select display_name from users where id = @userId for update", new { userId }, tx, cancellationToken: ct));
        if (name is null) return Results.Conflict(NameRequired);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from group_members where user_id = @userId", new { userId }, tx, cancellationToken: ct)) >= MaxGroupsPerUser
            ? Results.Conflict(GroupLimit) : null;
    }

    // 31^8 codes make a collision unlikely; retry a few times rather than fail (same as ShareCodes).
    static async Task<T> WithNewCodeAsync<T>(Func<string, Task<T>> run, NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await conn.ExecuteAsync(new CommandDefinition("savepoint invite_code", transaction: tx, cancellationToken: ct));
            try { return await run(ShareCodes.Generate()); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation && e.ConstraintName == "groups_invite_code_key" && attempt < 3)
            {
                await conn.ExecuteAsync(new CommandDefinition("rollback to savepoint invite_code", transaction: tx, cancellationToken: ct));
            }
        }
    }

    // Members of a group the viewer belongs to; null otherwise. A member who blocked the viewer is left out, as everywhere
    // else a blocked person sees nothing of the blocker. Requests are listed for admins only.
    public static async Task<GroupDetail?> ReadAsync(NpgsqlConnection conn, Guid userId, Guid id, CancellationToken ct)
    {
        var group = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Name, string TimeZone, string InviteCode, string Role, bool ShareTotals)>(
            new CommandDefinition("""
                select g.id, g.name, g.time_zone, g.invite_code, m.role, m.share_totals
                from groups g join group_members m on m.group_id = g.id
                where g.id = @id and m.user_id = @userId and m.accepted_at is not null
                """, new { id, userId }, cancellationToken: ct));
        if (group.Id == Guid.Empty) return null;
        var rows = (await conn.QueryAsync<GroupMember>(new CommandDefinition($"""
            select u.id, u.display_name, m.role, u.avatar_id, {SocialEndpoints.PreviousName},
                case when m.accepted_at is null then u.created_at end as joined_at, null::text as similar_to
            from group_members m join users u on u.id = m.user_id
            where m.group_id = @id and {SocialEndpoints.NotBlocked("m.user_id", "@userId")}
                and (m.accepted_at is not null or @admin)
            order by m.role, u.display_name, u.id
            """, new { id, userId, admin = group.Role == "admin" }, cancellationToken: ct))).ToList();
        var members = rows.Where(r => r.JoinedAt is null).ToList();
        var requests = rows.Where(r => r.JoinedAt is not null).Select(r => r with
        {
            SimilarTo = members.FirstOrDefault(m => MeEndpoints.NameSkeleton(m.DisplayName) == MeEndpoints.NameSkeleton(r.DisplayName))?.DisplayName,
        }).ToList();
        return new(group.Id, group.Name, group.TimeZone, group.InviteCode, group.Role, group.ShareTotals, members, requests);
    }

    const string WeekActivities = """
        a.user_id in (select user_id from members) and a.source <> 'garmin'
            and a.started_at >= @weekStart::timestamp at time zone @timeZone
            and a.started_at < (@weekStart + 7)::timestamp at time zone @timeZone
        """;

    // One week (Monday, in the group's time zone) for the members who share their totals. week null = the current week.
    public static async Task<Leaderboard?> ReadLeaderboardAsync(NpgsqlConnection conn, Guid userId, Guid id, string? sportGroup,
        string metric, DateOnly? week, DateTimeOffset now, CancellationToken ct)
    {
        var timeZone = await conn.ExecuteScalarAsync<string?>(new CommandDefinition($"""
            select time_zone from groups where id = @id and ({Membership}) is not null
            """, new { id, userId }, cancellationToken: ct));
        if (timeZone is null) return null;
        var weekStart = Goals.PeriodStart("week", week ?? await conn.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "select (@now at time zone @timeZone)::date", new { now, timeZone }, cancellationToken: ct)));
        var rows = await conn.QueryAsync<LeaderboardRow>(new CommandDefinition($"""
            with members as (
                select m.user_id from group_members m
                where m.group_id = @id and m.accepted_at is not null and m.share_totals
                    and {SocialEndpoints.NotBlocked("m.user_id", "@userId")}
            ), {SportGroups.Cte}, {SportGroups.Effective(WeekActivities)}
            select u.id as user_id, u.display_name, u.avatar_id, coalesce(t.value, 0) as value
            from members join users u on u.id = members.user_id
            left join (
                select e.user_id, (case @metric when 'distance' then sum(e.distance_m) when 'duration' then sum(e.duration_s)
                    else count(distinct e.id) end)::double precision as value
                from effective e
                where case when @sportGroup::text is null then not e.part else e.sport_group = @sportGroup end
                group by e.user_id
            ) t on t.user_id = u.id
            order by value desc, u.display_name, u.id
            """, new { id, userId, timeZone, weekStart, metric, sportGroup,
            groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct));
        return new(weekStart, rows.ToList());
    }
}
