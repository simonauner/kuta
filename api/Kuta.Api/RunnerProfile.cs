using System.Security.Claims;
using Dapper;
using Npgsql;

public sealed record CalendarDay(DateOnly Day, int Count, double DistanceM);
public sealed record RunnerSummary(IReadOnlyList<CalendarDay> Days);

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
        // 53 full weeks plus the current one, in the viewer's time zone.
        var days = (await conn.QueryAsync<CalendarDay>(new CommandDefinition($"""
            select (a.started_at at time zone @timeZone)::date as day, count(*)::int as count, sum(a.distance_m) as distance_m
            from activities a
            where a.user_id = @runnerId and {SocialEndpoints.CanRead} and a.started_at >= now() - interval '378 days'
            group by 1 order by 1
            """, new { userId, runnerId, timeZone }, cancellationToken: ct))).ToList();
        return new RunnerSummary(days);
    }
}
