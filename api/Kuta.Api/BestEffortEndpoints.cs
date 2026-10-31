using System.Security.Claims;
using Dapper;
using Npgsql;

public sealed record StoredBestEffort(Guid ActivityId, int DistanceM, double ElapsedS, double StartOffsetS, DateTime AchievedAt);

public static class BestEffortEndpoints
{
    public static void MapBestEfforts(this RouteGroupBuilder api)
    {
        var activities = api.MapGroup("/activities").RequireAuthorization();
        activities.MapGet("/{id:guid}/best-efforts", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            if (!await conn.ExecuteScalarAsync<bool>(
                    $"select exists (select 1 from activities a where a.id = @id and {SocialEndpoints.CanRead})", new { id, userId }))
                return Results.NotFound();
            return Results.Ok(await conn.QueryAsync<StoredBestEffort>(
                """
                select activity_id, distance_m, elapsed_s, start_offset_s, achieved_at
                from best_efforts where activity_id = @id order by distance_m
                """, new { id, userId }));
        });

        activities.MapPost("/{id:guid}/best-efforts/recalculate", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var userId = user.GetUserId();
            var activity = await conn.QuerySingleOrDefaultAsync<EffortActivity>(
                "select sport, started_at from activities where id = @id and user_id = @userId for update", new { id, userId }, tx);
            if (activity is null) return Results.NotFound();
            int[] t = [];
            float?[]? dist = null;
            // Read nullable array elements explicitly; Dapper sees PostgreSQL arrays as System.Array.
            await using (var command = new NpgsqlCommand("select t, dist from activity_streams where activity_id = @id", conn, tx))
            {
                command.Parameters.AddWithValue("id", id);
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    t = reader.GetFieldValue<int[]>(0);
                    dist = reader.IsDBNull(1) ? null : reader.GetFieldValue<float?[]>(1);
                }
            }
            await BestEfforts.ReplaceAsync(conn, tx, id, userId, activity.Sport, activity.StartedAt,
                t, dist, await Multisport.LoadAsync(conn, id, tx));
            await tx.CommitAsync();
            return Results.NoContent();
        });

        api.MapGet("/records", async (int? year, string? tag, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (year is < 1 or > 9998)
                return Results.BadRequest(new ApiError("Ange ett år mellan 1 och 9998.", "invalidRecordYear"));
            if (ActivityInput.ValidateTags(tag is null ? null : [tag]) is { } invalid)
                return Results.BadRequest(invalid);
            var from = year is { } y ? new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc) : (DateTime?)null;
            var until = from?.AddYears(1);
            await using var conn = await db.OpenConnectionAsync();
            return Results.Ok(await conn.QueryAsync<StoredBestEffort>(
                """
                select distinct on (distance_m) activity_id, distance_m, elapsed_s, start_offset_s, achieved_at
                from best_efforts
                where user_id = @userId
                  and activity_id in (select id from activities where user_id = @userId and tags @> @tags::text[])
                  and (@from::timestamptz is null or achieved_at >= @from)
                  and (@until::timestamptz is null or achieved_at < @until)
                order by distance_m, elapsed_s, achieved_at, activity_id
                """, new { userId = user.GetUserId(), from, until, tags = tag is null ? Array.Empty<string>() : [tag] }));
        }).RequireAuthorization();
    }

    private sealed record EffortActivity(string Sport, DateTime StartedAt);
}
