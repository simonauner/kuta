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
            return Results.Ok(await RecordTrends.ReadActivityAsync(conn, id, userId));
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
                  and activity_id in (select id from activities where user_id = @userId and not exclude_from_records and tags @> @tags::text[])
                  and (@from::timestamptz is null or achieved_at >= @from)
                  and (@until::timestamptz is null or achieved_at < @until)
                order by distance_m, elapsed_s, achieved_at, activity_id
                """, new { userId = user.GetUserId(), from, until, tags = tag is null ? Array.Empty<string>() : [tag] }));
        }).RequireAuthorization();

        api.MapGet("/records/trend", async (int distanceM, string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (BestEffortCalculator.Meters(distanceM) is null)
                return Results.BadRequest(new ApiError("Välj en av rekorddistanserna.", "invalidRecordDistance"));
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await RecordTrends.ReadAsync(conn, user.GetUserId(), distanceM, timeZone, ct));
        }).RequireAuthorization();

        api.MapGet("/training-load", async (string? timeZone, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(ct);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, ct))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await TrainingLoads.ReadAsync(conn, user.GetUserId(), timeZone, DateTime.UtcNow, ct));
        }).RequireAuthorization();

        api.MapPut("/me/threshold-speed", async (ThresholdSpeedRequest req, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (req.ThresholdSpeedMS is { } speed && !(speed >= 1000d / 3600 && speed < 10))
                return Results.BadRequest(new ApiError("Ange ett tröskeltempo mellan 1:40 och 60:00 per km.", "invalidThresholdSpeed"));
            await using var conn = await db.OpenConnectionAsync(ct);
            var updated = await conn.ExecuteAsync(new CommandDefinition(
                "update users set threshold_speed_m_s = @speed where id = @id",
                new { id = user.GetUserId(), speed = req.ThresholdSpeedMS }, cancellationToken: ct));
            return updated == 0
                ? Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"))
                : Results.NoContent();
        }).RequireAuthorization();

        api.MapGet("/records/predictions", async (NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return Results.Ok(await RecordTrends.PredictAsync(conn, user.GetUserId(), DateTime.UtcNow, ct));
        }).RequireAuthorization();
    }

    private sealed record EffortActivity(string Sport, DateTime StartedAt);
}
