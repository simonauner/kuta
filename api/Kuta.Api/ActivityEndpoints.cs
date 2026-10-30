using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using Dynastream.Fit;
using Npgsql;

// Typade records, inte dynamic: Dapper-rader serialiseras som dictionary med snake_case-nycklar.
public record ActivitySummary(
    Guid Id, string Sport, string? Title, System.DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, Guid? ShoeId);

public static class ActivityEndpoints
{
    private const int MaxFitFileBytes = 20 * 1024 * 1024;

    public static void MapActivities(this RouteGroupBuilder api)
    {
        var activities = api.MapGroup("/activities").RequireAuthorization();

        // Inloggad användares senaste pass
        activities.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var rows = await conn.QueryAsync<ActivitySummary>(
                """
                select id, sport, title, started_at, duration_s, distance_m, avg_hr, shoe_id
                from activities
                where user_id = @userId
                order by started_at desc
                limit 50
                """,
                new { userId = user.GetUserId() });
            return Results.Ok(rows);
        });

        // Uppladdning av en FIT-fil: tolka den, spara råfilen (bara med pulssamtycke) och skriv passet med tidsserier
        // i en transaktion. Samma fil igen ger 409 (raw_file_sha256 är unik per användare).
        activities.MapPost("", async (IFormFile? file, NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
                return Results.BadRequest(new ApiError("Välj en FIT-fil att ladda upp.", "fileRequired"));
            if (file.Length > MaxFitFileBytes)
                return Results.BadRequest(new ApiError("Filen är för stor (högst 20 MB).", "fileTooLarge"));

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var sha256 = SHA256.HashData(bytes);
            var userId = user.GetUserId();

            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var profile = await MeEndpoints.LoadProfileAsync(conn, userId);
            if (profile is null) return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            if (await conn.ExecuteScalarAsync<bool>(
                    "select exists (select 1 from activities where user_id = @userId and raw_file_sha256 = @sha256)",
                    new { userId, sha256 }))
                return Results.Conflict(new ApiError("Det här passet är redan uppladdat.", "duplicateActivity"));

            ParsedActivity? parsed;
            try { parsed = FitImport.Parse(bytes); }
            catch (FitException) { parsed = null; }
            if (parsed is null) return Results.BadRequest(new ApiError("Filen kunde inte läsas som ett pass från en FIT-fil.", "invalidFit"));
            // Utan samtycke sparas varken pulsen eller råfilen (som också innehåller puls); hashen räcker för dubbletter.
            string? key = null;
            if (profile.HrConsent)
            {
                key = $"fit/{userId}/{Convert.ToHexStringLower(sha256)}.fit";
                await store.PutAsync(key, bytes, cancellationToken);
            }
            else parsed = parsed.WithoutHeartRate();

            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            Guid id;
            try
            {
                id = await conn.ExecuteScalarAsync<Guid>(
                    """
                    insert into activities (user_id, source, sport, started_at, duration_s, moving_s, distance_m,
                                            avg_hr, max_hr, avg_speed_mps, elevation_gain_m, route, raw_file_key, raw_file_sha256)
                    values (@userId, 'fit', @Sport, @StartedAt, @DurationS, @MovingS, @DistanceM,
                            @AvgHr, @MaxHr, @AvgSpeedMps, @ElevationGainM, @route::geography, @key, @sha256)
                    returning id
                    """,
                    new
                    {
                        userId, parsed.Sport, parsed.StartedAt, parsed.DurationS, parsed.MovingS, parsed.DistanceM,
                        parsed.AvgHr, parsed.MaxHr, parsed.AvgSpeedMps, parsed.ElevationGainM,
                        route = (object?)parsed.Route ?? DBNull.Value, key, sha256,
                    }, tx);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Samma fil uppladdad samtidigt i en annan förfrågan
                return Results.Conflict(new ApiError("Det här passet är redan uppladdat.", "duplicateActivity"));
            }
            await conn.ExecuteAsync(
                """
                insert into activity_streams (activity_id, t, dist, hr, speed, alt, lat, lon)
                values (@id, @T, @Dist, @Hr, @Speed, @Alt, @Lat, @Lon)
                """,
                new { id, parsed.T, parsed.Dist, parsed.Hr, parsed.Speed, parsed.Alt, parsed.Lat, parsed.Lon }, tx);
            await tx.CommitAsync(cancellationToken);

            return Results.Created($"/api/activities/{id}", new ActivitySummary(
                id, parsed.Sport, null, parsed.StartedAt, parsed.DurationS, parsed.DistanceM, parsed.AvgHr, null));
        })
        // CSRF-token kontrolleras av filtret i AuthEndpoints (X-CSRF-TOKEN), inte av ramverkets formulärkontroll
        .DisableAntiforgery();

        // Tar bort ett eget pass med tidsserier (kaskad) och råfil. Någon annans pass ger 404, som ett som inte finns.
        // Råfilen först: misslyckas raderingen ligger passet kvar och kan tas bort igen, i stället för att filen
        // (som innehåller puls) blir kvar utan något som pekar på den.
        activities.MapDelete("/{id:guid}", async (Guid id, NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var userId = user.GetUserId();
            var activity = await conn.QuerySingleOrDefaultAsync<OwnedActivity>(
                "select id, raw_file_key from activities where id = @id and user_id = @userId", new { id, userId });
            if (activity is null) return Results.NotFound();

            if (activity.RawFileKey is { } key) await store.DeleteAsync([key], cancellationToken);
            await conn.ExecuteAsync("delete from activities where id = @id and user_id = @userId", new { id, userId });
            return Results.NoContent();
        });
    }

    private record OwnedActivity(Guid Id, string? RawFileKey);
}
