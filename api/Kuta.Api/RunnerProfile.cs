using System.Security.Claims;
using Dapper;
using Npgsql;

public sealed record CalendarDay(DateOnly Day, int Count, double DistanceM);
public sealed record SummaryTotals(int Count, double DistanceM, double DurationS);
public sealed record SportCount(string Sport, int Count);
// JoinedAt is rounded to the month: enough to show how long someone has run with Kuta.
public sealed record RunnerSummary(DateTime JoinedAt, SummaryTotals ThisYear, SummaryTotals AllTime,
    IReadOnlyList<SportCount> TopSports, IReadOnlyList<CalendarDay> Days, IReadOnlyList<StoredBestEffort> Records,
    int FollowerCount, int FollowingCount);

// What a profile shows beyond the activity list, computed only from activities the viewer may read.
public static class RunnerProfile
{
    public static void MapRunnerProfile(this RouteGroupBuilder api)
    {
        api.MapGet("/runners/{id:guid}/summary", async (Guid id, string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from users where id = @id)", new { id }, cancellationToken: ct)))
                return Results.NotFound();
            return Results.Ok(await ReadAsync(conn, user.GetUserId(), id, timeZone, ct));
        }).RequireAuthorization();
    }

    public static async Task<RunnerSummary> ReadAsync(NpgsqlConnection conn, Guid userId, Guid runnerId, string timeZone,
        CancellationToken ct)
    {
        var args = new { userId, runnerId, timeZone };
        // 53 full weeks plus the current one, in the viewer's time zone.
        var days = (await conn.QueryAsync<CalendarDay>(new CommandDefinition($"""
            select (a.started_at at time zone @timeZone)::date as day, count(*)::int as count, sum(a.distance_m) as distance_m
            from activities a
            where a.user_id = @runnerId and {SocialEndpoints.CanRead} and a.started_at >= now() - interval '378 days'
            group by 1 order by 1
            """, args, cancellationToken: ct))).ToList();
        // Only approved relationships count; who they are is shown to the owner alone, under Settings.
        var (joinedAt, followerCount, followingCount) = await conn.QuerySingleAsync<(DateTime, int, int)>(new CommandDefinition("""
            select date_trunc('month', created_at),
                (select count(*)::int from follows where following_id = @runnerId and accepted_at is not null),
                (select count(*)::int from follows where follower_id = @runnerId and accepted_at is not null)
            from users where id = @runnerId
            """, args, cancellationToken: ct));
        async Task<SummaryTotals> Totals(bool thisYear) => await conn.QuerySingleAsync<SummaryTotals>(new CommandDefinition($"""
            select count(*)::int as count, coalesce(sum(a.distance_m), 0) as distance_m, coalesce(sum(a.duration_s), 0)::float8 as duration_s
            from activities a
            where a.user_id = @runnerId and {SocialEndpoints.CanRead}
              and (not @thisYear or a.started_at >= date_trunc('year', now() at time zone @timeZone) at time zone @timeZone)
            """, new { userId, runnerId, timeZone, thisYear }, cancellationToken: ct));
        var topSports = (await conn.QueryAsync<SportCount>(new CommandDefinition($"""
            select a.sport, count(*)::int as count
            from activities a
            where a.user_id = @runnerId and {SocialEndpoints.CanRead}
            group by 1 order by 2 desc, 1 limit 3
            """, args, cancellationToken: ct))).ToList();
        // Personal bests among readable activities, with the same tie-breaking as /api/records.
        var records = (await conn.QueryAsync<StoredBestEffort>(new CommandDefinition($"""
            select distinct on (b.distance_m) b.activity_id, b.distance_m, b.elapsed_s, b.start_offset_s, b.achieved_at
            from best_efforts b join activities a on a.id = b.activity_id
            where a.user_id = @runnerId and not a.exclude_from_records and {SocialEndpoints.CanRead}
            order by b.distance_m, b.elapsed_s, b.achieved_at, b.activity_id
            """, args, cancellationToken: ct))).ToList();
        return new RunnerSummary(joinedAt, await Totals(true), await Totals(false), topSports, days, records,
            followerCount, followingCount);
    }
}
