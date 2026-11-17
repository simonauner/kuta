using System.Security.Claims;
using Dapper;
using Npgsql;

// Visibility null keeps the current one; description, place and sport group are replaced (null clears them).
public record GroupRequest(string? Name, string? TimeZone = null, string? Visibility = null, string? Description = null,
    string? Place = null, string? SportGroup = null);
public record ShareTotalsRequest(bool ShareTotals);
// Status: "member" or "pending" (waiting for an admin).
public record GroupSummary(Guid Id, string Name, string Role, string Status, int MemberCount);
// JoinedAt and SimilarTo are set only for requests, as for follow requests: they help an admin spot impersonation.
public record GroupMember(Guid Id, string DisplayName, string Role, Guid? AvatarId, string? PreviousName,
    DateTime? JoinedAt = null, string? SimilarTo = null);
// The members themselves are paged (GET /api/groups/{id}/members): a club can have up to 1000.
public record GroupDetail(Guid Id, string Name, string TimeZone, string InviteCode, string Role, bool ShareTotals,
    int MemberCount, IReadOnlyList<GroupMember> Requests, string Visibility, string? Description,
    string? Place, string? SportGroup, IReadOnlyList<GroupGoal>? Goals = null);
// A shared goal with the same progress shape as a personal goal, and the viewer's own part of the current period.
public record GroupGoal(GoalProgress Progress, double OwnValue);
// What anyone signed in may see of an open club. Status for the viewer: "none", "pending" or "member".
public record ClubSummary(Guid Id, string Name, string? Place, string? SportGroup, string? Description, int MemberCount,
    DateTime CreatedAt, string Status);
// Status for the visitor: "none", "pending" or "member".
public record GroupInvite(Guid Id, string Name, int MemberCount, string Status);
public record LeaderboardRow(Guid UserId, string DisplayName, Guid? AvatarId, double Value);
// GarminData: the totals include Garmin activities, so the leaderboard needs Garmin's attribution.
public record Leaderboard(DateOnly WeekStart, IReadOnlyList<LeaderboardRow> Rows, bool GarminData = false);

// Groups are joined through an invite link or, for open clubs, from search; an admin approves every request. Membership never grants access to activities (the group feed shows only public ones): members see each other and a weekly leaderboard of
// the totals each member chose to share (all own activities, also private ones, but never Garmin activities, which are
// shown only to their owner until Garmin has had 30 days' notice of showing them to others).
public static class Groups
{
    public const int MaxMembers = 100;
    // Real clubs are larger; open clubs are the ones that represent them.
    public const int MaxClubMembers = 1000;
    public const int MaxGroupsPerUser = 20;
    public const int MemberPageSize = 50;
    public const int MaxGoals = 10;

    static readonly ApiError InvalidName = new("Ange ett namn på högst 100 tecken.", "invalidGroupName");
    static readonly ApiError GroupLimit = new($"Du kan vara med i högst {MaxGroupsPerUser} grupper.", "groupLimit");
    static readonly ApiError GroupFull = new("Gruppen kan inte ta emot fler medlemmar.", "groupFull");
    static readonly ApiError InvalidSettings = new("Kontrollera synlighet, ort (högst 100 tecken), sport och beskrivning (högst 500 tecken).",
        "invalidGroupSettings");
    static readonly ApiError InvalidClubSearch = new("Sök på minst tre bokstäver eller siffror.", "invalidClubSearch");
    static readonly ApiError NameRequired = new("Välj ett namn först.", "nameRequired");
    static readonly ApiError InvalidLeaderboard = new("Kontrollera sport, typ och vecka.", "invalidLeaderboard");
    static readonly ApiError GoalLimit = new($"En grupp kan ha högst {MaxGoals} mål.", "groupGoalLimit");
    static readonly ApiError InvalidTimeZone = new("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone");

    public static string? NormalizeName(string? name) => name?.Trim() is { Length: >= 1 and <= 100 } trimmed ? trimmed : null;

    static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static ApiError? ValidateSettings(GroupRequest req) =>
        (req.Visibility is not (null or "private" or "open"))
        || Blank(req.Description)?.Length > 500 || Blank(req.Place)?.Length > 100
        || (req.SportGroup is not null && !SportGroups.IsValid(req.SportGroup))
            ? InvalidSettings : null;

    const string ClubColumns = """
        g.id, g.name, g.place, g.sport_group, g.description,
            (select count(*) from group_members c where c.group_id = g.id and c.accepted_at is not null)::int as member_count,
            g.created_at,
            case when m.user_id is null then 'none' when m.accepted_at is null then 'pending' else 'member' end as status
        """;

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
            if (ValidateSettings(req) is { } invalid) return Results.BadRequest(invalid);
            var timeZone = req.TimeZone ?? "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct)) return Results.BadRequest(InvalidTimeZone);
            var userId = user.GetUserId();
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (await LockMemberAsync(conn, tx, userId, ct) is { } error) return error;
            var id = await WithNewCodeAsync(code => conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                with g as (insert into groups (name, name_skeleton, invite_code, time_zone, visibility, description, place, sport_group)
                    values (@name, @skeleton, @code, @timeZone, coalesce(@Visibility, 'private'), @description, @place, @SportGroup)
                    returning id)
                insert into group_members (group_id, user_id, role, accepted_at) select id, @userId, 'admin', now() from g
                returning group_id
                """, new { name, skeleton = MeEndpoints.NameSkeleton(name), code, timeZone, userId, req.Visibility,
                description = Blank(req.Description), place = Blank(req.Place), req.SportGroup }, tx, cancellationToken: ct)),
                conn, tx, ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/groups/{id}", new { id });
        });
        groups.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var detail = await ReadAsync(conn, user.GetUserId(), id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        // Only members see the group's activities.
        groups.MapGet("/{id:guid}/feed", async (Guid id, DateTimeOffset? beforeStartedAt, Guid? beforeId, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (beforeStartedAt.HasValue != beforeId.HasValue)
                return Results.BadRequest(new ApiError("Sidmarkören är ogiltig. Ladda om sidan.", "invalidActivityPage"));
            await using var conn = await db.OpenConnectionAsync(ct);
            var userId = user.GetUserId();
            if (await conn.ExecuteScalarAsync<string?>(new CommandDefinition(Membership, new { id, userId }, cancellationToken: ct)) is null)
                return Results.NotFound();
            return Results.Ok(await SocialEndpoints.ReadFeedAsync(conn, userId, beforeStartedAt, beforeId, groupId: id));
        });
        groups.MapGet("/{id:guid}/members", async (Guid id, int? offset, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var members = await ReadMembersAsync(conn, user.GetUserId(), id, Math.Max(0, offset ?? 0), ct);
            return members is null ? Results.NotFound() : Results.Ok(members);
        });
        groups.MapPut("/{id:guid}", async (Guid id, GroupRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (NormalizeName(req.Name) is not { } name) return Results.BadRequest(InvalidName);
            if (ValidateSettings(req) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition($"""
                update groups set name = @name, name_skeleton = @skeleton, visibility = coalesce(@Visibility, visibility),
                    description = @description, place = @place, sport_group = @SportGroup
                where id = @id and ({Membership}) = 'admin'
                """, new { id, name, skeleton = MeEndpoints.NameSkeleton(name), req.Visibility, description = Blank(req.Description),
                place = Blank(req.Place), req.SportGroup, userId = user.GetUserId() }, cancellationToken: ct)) > 0
                ? Results.NoContent() : Results.NotFound();
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
            var group = await conn.QuerySingleOrDefaultAsync<(string? Role, string Visibility)>(new CommandDefinition($"""
                select ({Membership}), visibility from groups where id = @id for update
                """, new { id, userId = user.GetUserId() }, tx, cancellationToken: ct));
            if (group.Role != "admin") return Results.NotFound();
            if (await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "select count(*) from group_members where group_id = @id and accepted_at is not null",
                    new { id }, tx, cancellationToken: ct)) >= (group.Visibility == "open" ? MaxClubMembers : MaxMembers))
                return Results.Conflict(GroupFull);
            var updated = await conn.ExecuteAsync(new CommandDefinition("""
                update group_members set accepted_at = coalesce(accepted_at, now()) where group_id = @id and user_id = @memberId
                """, new { id, memberId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            if (updated == 0) return Results.NotFound();
            await Push.EnqueueIfPendingAsync(conn, memberId, ct);
            return Results.NoContent();
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
        groups.MapPost("/{id:guid}/goals", async (Guid id, GoalRequest req, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (Goals.Validate(req with { Privacy = null }) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            // Locking the group serialises concurrent creates, so the limit holds.
            var count = await conn.ExecuteScalarAsync<int?>(new CommandDefinition($"""
                select (select count(*) from group_goals where group_id = g.id)::int from groups g
                where g.id = @id and ({Membership}) = 'admin' for update of g
                """, new { id, userId = user.GetUserId() }, tx, cancellationToken: ct));
            if (count is null) return Results.NotFound();
            if (count >= MaxGoals) return Results.Conflict(GoalLimit);
            var goalId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into group_goals (group_id, sport_group, metric, period, target, starts_on)
                select g.id, @SportGroup, @Metric, @Period, @Target, (@now at time zone g.time_zone)::date
                from groups g where g.id = @id
                returning id
                """, new { id, req.SportGroup, req.Metric, req.Period, req.Target, now = DateTimeOffset.UtcNow }, tx,
                cancellationToken: ct));
            await tx.CommitAsync(ct);
            return Results.Created($"/api/groups/{id}/goals/{goalId}", new { id = goalId });
        });
        // The start date is kept on edit, as for personal goals.
        groups.MapPut("/{id:guid}/goals/{goalId:guid}", async (Guid id, Guid goalId, GoalRequest req, NpgsqlDataSource db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (Goals.Validate(req with { Privacy = null }) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition($"""
                update group_goals set sport_group = @SportGroup, metric = @Metric, period = @Period, target = @Target
                where id = @goalId and group_id = @id and ({Membership}) = 'admin'
                """, new { id, goalId, userId = user.GetUserId(), req.SportGroup, req.Metric, req.Period, req.Target },
                cancellationToken: ct)) > 0 ? Results.NoContent() : Results.NotFound();
        });
        groups.MapDelete("/{id:guid}/goals/{goalId:guid}", async (Guid id, Guid goalId, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition($"""
                delete from group_goals where id = @goalId and group_id = @id and ({Membership}) = 'admin'
                """, new { id, goalId, userId = user.GetUserId() }, cancellationToken: ct)) > 0
                ? Results.NoContent() : Results.NotFound();
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
            return await JoinAsync(conn, user.GetUserId(), "invite_code = @code", new { code = code.ToLowerInvariant() }, ct);
        }).RequireRateLimiting("follows");

        // Open clubs: searchable by name, never listed in full; the members stay visible to members only.
        var clubs = api.MapGroup("/clubs").RequireAuthorization();
        clubs.MapGet("", async (string? query, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var skeleton = MeEndpoints.NameSkeleton(query ?? "");
            if (skeleton.Length < 3) return Results.BadRequest(InvalidClubSearch);
            await using var conn = await db.OpenConnectionAsync(ct);
            return Results.Ok(await SearchClubsAsync(conn, user.GetUserId(), skeleton, ct));
        }).RequireRateLimiting("clubSearch");
        clubs.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var club = await conn.QuerySingleOrDefaultAsync<ClubSummary>(new CommandDefinition($"""
                select {ClubColumns}
                from groups g left join group_members m on m.group_id = g.id and m.user_id = @userId
                where g.id = @id and g.visibility = 'open'
                """, new { id, userId = user.GetUserId() }, cancellationToken: ct));
            return club is null ? Results.NotFound() : Results.Ok(club);
        });
        clubs.MapPost("/{id:guid}/join", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await JoinAsync(conn, user.GetUserId(), "id = @id and visibility = 'open'", new { id }, ct);
        }).RequireRateLimiting("follows");
    }

    // Clubs whose name contains the search, or whose name is inside it (so "Hässelby SK Friidrott" finds "Hässelby SK"
    // before a duplicate is created); exact names first, then the largest clubs. Names under five letters are not matched
    // inside longer searches, or "IK" would match half of them. The skeleton has only letters and digits, so it needs no
    // escaping for like.
    // shortcut: abbreviations ("SK" for "Sportklubb") are not similar; compare words if duplicates slip through.
    public static async Task<IReadOnlyList<ClubSummary>> SearchClubsAsync(NpgsqlConnection conn, Guid userId, string skeleton,
        CancellationToken ct) =>
        (await conn.QueryAsync<ClubSummary>(new CommandDefinition($"""
            select {ClubColumns}
            from groups g left join group_members m on m.group_id = g.id and m.user_id = @userId
            where g.visibility = 'open' and (g.name_skeleton like '%' || @skeleton || '%'
                or (length(g.name_skeleton) >= 5 and @skeleton like '%' || g.name_skeleton || '%'))
            order by g.name_skeleton = @skeleton desc, member_count desc, g.name, g.id
            limit 20
            """, new { userId, skeleton }, cancellationToken: ct))).ToList();

    // A request to join the group matching the condition (by invite code, or an open club by id).
    static async Task<IResult> JoinAsync(NpgsqlConnection conn, Guid userId, string condition, object args, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            $"select id from groups where {condition}", args, tx, cancellationToken: ct));
        if (id is null) return Results.NotFound();
        var requested = !await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from group_members where group_id = @id and user_id = @userId)",
            new { id, userId }, tx, cancellationToken: ct));
        if (requested)
        {
            if (await LockMemberAsync(conn, tx, userId, ct) is { } error) return error;
            await conn.ExecuteAsync(new CommandDefinition("""
                insert into group_members (group_id, user_id) values (@id, @userId) on conflict do nothing
                """, new { id, userId }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        if (requested)
            foreach (var admin in await conn.QueryAsync<Guid>(new CommandDefinition(
                "select user_id from group_members where group_id = @id and role = 'admin'", new { id }, cancellationToken: ct)))
                await Push.EnqueueIfPendingAsync(conn, admin, ct);
        return Results.Ok(new { id });
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

    // A member who blocked the viewer is left out, as everywhere else a blocked person sees nothing of the blocker.
    static string VisibleMember(string viewer) => $"m.group_id = @id and {SocialEndpoints.NotBlocked("m.user_id", viewer)}";

    // A group the viewer belongs to; null otherwise. Requests are listed for admins only, with a warning when a name looks
    // like a member's.
    public static async Task<GroupDetail?> ReadAsync(NpgsqlConnection conn, Guid userId, Guid id, CancellationToken ct)
    {
        var group = await conn.QuerySingleOrDefaultAsync<GroupRow>(new CommandDefinition($"""
            select g.id, g.name, g.time_zone, g.invite_code, m.role, m.share_totals, g.visibility, g.description, g.place, g.sport_group,
                (select count(*) from group_members m where {VisibleMember("@userId")} and m.accepted_at is not null)::int as member_count
            from groups g join group_members m on m.group_id = g.id
            where g.id = @id and m.user_id = @userId and m.accepted_at is not null
            """, new { id, userId }, cancellationToken: ct));
        if (group is null) return null;
        List<GroupMember> requests = [];
        if (group.Role == "admin")
        {
            requests = (await conn.QueryAsync<GroupMember>(new CommandDefinition($"""
                select u.id, u.display_name, m.role, u.avatar_id, {SocialEndpoints.PreviousName}, u.created_at as joined_at,
                    null::text as similar_to
                from group_members m join users u on u.id = m.user_id
                where {VisibleMember("@userId")} and m.accepted_at is null
                order by u.display_name, u.id
                """, new { id, userId }, cancellationToken: ct))).ToList();
            if (requests.Count > 0)
            {
                var names = (await conn.QueryAsync<string>(new CommandDefinition($"""
                    select u.display_name from group_members m join users u on u.id = m.user_id
                    where {VisibleMember("@userId")} and m.accepted_at is not null
                    """, new { id, userId }, cancellationToken: ct))).Select(n => (Name: n, Skeleton: MeEndpoints.NameSkeleton(n))).ToList();
                requests = requests.Select(r => r with
                {
                    SimilarTo = names.FirstOrDefault(n => n.Skeleton == MeEndpoints.NameSkeleton(r.DisplayName)).Name,
                }).ToList();
            }
        }
        return new(group.Id, group.Name, group.TimeZone, group.InviteCode, group.Role, group.ShareTotals, group.MemberCount,
            requests, group.Visibility, group.Description, group.Place, group.SportGroup,
            await ReadGoalsAsync(conn, userId, id, group.TimeZone, DateTimeOffset.UtcNow, ct));
    }

    // One page of a group's members, admins first; null when the viewer is not a member.
    public static async Task<IReadOnlyList<GroupMember>?> ReadMembersAsync(NpgsqlConnection conn, Guid userId, Guid id, int offset,
        CancellationToken ct)
    {
        if (await conn.ExecuteScalarAsync<string?>(new CommandDefinition(Membership, new { id, userId }, cancellationToken: ct)) is null)
            return null;
        return (await conn.QueryAsync<GroupMember>(new CommandDefinition($"""
            select u.id, u.display_name, m.role, u.avatar_id, {SocialEndpoints.PreviousName}, null::timestamptz as joined_at,
                null::text as similar_to
            from group_members m join users u on u.id = m.user_id
            where {VisibleMember("@userId")} and m.accepted_at is not null
            order by m.role, u.display_name, u.id
            limit @MemberPageSize offset @offset
            """, new { id, userId, offset, MemberPageSize }, cancellationToken: ct))).ToList();
    }

    record GroupRow(Guid Id, string Name, string TimeZone, string InviteCode, string Role, bool ShareTotals, string Visibility,
        string? Description, string? Place, string? SportGroup, int MemberCount);

    // The members whose totals count for the viewer: those who share them, except anyone who blocked the viewer. The
    // goal uses the same rows as the leaderboard, so its total never reveals a hidden member's totals.
    static readonly string SharingMembers = $"""
        members as (
            select m.user_id from group_members m
            where m.group_id = @id and m.accepted_at is not null and m.share_totals
                and {SocialEndpoints.NotBlocked("m.user_id", "@userId")}
        )
        """;

    static string MetricValue(string filter = "") => $"""
        (case g.metric when 'distance' then sum(e.distance_m){filter} when 'duration' then sum(e.duration_s){filter}
            else count(distinct e.id){filter} end)
        """;

    // The viewer is a member. Oldest goal first.
    public static async Task<IReadOnlyList<GroupGoal>> ReadGoalsAsync(NpgsqlConnection conn, Guid userId, Guid id, string timeZone,
        DateTimeOffset now, CancellationToken ct)
    {
        var goals = (await conn.QueryAsync<StoredGoal>(new CommandDefinition("""
            select id, sport_group, metric, period, target, true as recurring, starts_on, 'private' as privacy
            from group_goals where group_id = @id
            order by created_at, id
            """, new { id }, cancellationToken: ct))).ToList();
        if (goals.Count == 0) return [];
        // Same as Goals.LoadAsync, over the sharing members, with the viewer's own part beside each total. The activities
        // are limited to the year of the earliest goal start, which covers every goal's first period.
        var buckets = (await conn.QueryAsync<(Guid GoalId, DateOnly Start, double Value, double Own, bool Garmin)>(new CommandDefinition($"""
            with {SharingMembers}, {SportGroups.Cte}, {SportGroups.Effective("""
                a.user_id in (select user_id from members) and a.source <> 'garmin'
                    and a.started_at >= (select min(date_trunc('year', starts_on::timestamp)) from group_goals
                        where group_id = @id) at time zone @timeZone
                """)}
            select g.id, b.period_start, b.value, b.own, b.garmin
            from group_goals g
            cross join lateral (
                select date_trunc(g.period, e.started_at at time zone @timeZone)::date as period_start,
                    {MetricValue()}::double precision as value,
                    coalesce({MetricValue(" filter (where e.user_id = @userId)")}, 0)::double precision as own,
                    bool_or(e.source = 'garmin') as garmin
                from effective e
                where (case when g.sport_group is null then not e.part else e.sport_group = g.sport_group end)
                    and e.started_at >= date_trunc(g.period, g.starts_on::timestamp) at time zone @timeZone
                group by 1
            ) b
            where g.group_id = @id
            """, new { id, userId, timeZone, groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys },
            cancellationToken: ct))).ToLookup(b => b.GoalId);
        var today = await conn.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "select (@now at time zone @timeZone)::date", new { now, timeZone }, cancellationToken: ct));
        return goals.Select(goal =>
        {
            var progress = Goals.WithGarmin(Goals.Evaluate(goal, today, buckets[goal.Id].ToDictionary(b => b.Start, b => b.Value)),
                buckets[goal.Id].Select(b => (b.Start, b.Garmin)));
            return new GroupGoal(progress, buckets[goal.Id].FirstOrDefault(b => b.Start == progress.Current.Start).Own);
        }).ToList();
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
        var rows = (await conn.QueryAsync<BoardRow>(new CommandDefinition($"""
            with {SharingMembers}, {SportGroups.Cte}, {SportGroups.Effective(WeekActivities)}
            select u.id as user_id, u.display_name, u.avatar_id, coalesce(t.value, 0) as value, coalesce(t.garmin, false) as garmin
            from members join users u on u.id = members.user_id
            left join (
                select e.user_id, (case @metric when 'distance' then sum(e.distance_m) when 'duration' then sum(e.duration_s)
                    else count(distinct e.id) end)::double precision as value, bool_or(e.source = 'garmin') as garmin
                from effective e
                where case when @sportGroup::text is null then not e.part else e.sport_group = @sportGroup end
                group by e.user_id
            ) t on t.user_id = u.id
            order by value desc, u.display_name, u.id
            """, new { id, userId, timeZone, weekStart, metric, sportGroup,
            groupedSports = SportGroups.GroupedSports, sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct))).ToList();
        // Which member's totals include Garmin data is not exposed, only whether the leaderboard does.
        return new(weekStart, rows.Select(r => new LeaderboardRow(r.UserId, r.DisplayName, r.AvatarId, r.Value)).ToList(),
            rows.Any(r => r.Garmin));
    }

    record BoardRow(Guid UserId, string DisplayName, Guid? AvatarId, double Value, bool Garmin);
}
