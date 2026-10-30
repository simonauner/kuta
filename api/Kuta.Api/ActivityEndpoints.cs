using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using Dynastream.Fit;
using Npgsql;

// Typade records, inte dynamic: Dapper-rader serialiseras som dictionary med snake_case-nycklar.
public record ActivitySummary(
    Guid Id, string Sport, string? Title, System.DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, Guid? ShoeId);

// Ett pass med tidsserier (en array per kanal, index = mätpunkt; null där klockan inte mätte).
public record ActivityDetail(
    Guid Id, string Sport, string? Title, System.DateTime StartedAt, int DurationS, int? MovingS, double DistanceM,
    short? AvgHr, short? MaxHr, float? AvgSpeedMps, float? ElevationGainM, string? Device, ActivityStreams? Streams,
    IEnumerable<ActivityLap> Laps);

public record ActivityStreams(
    int[] T, float?[]? Dist, short?[]? Hr, float?[]? Speed, float?[]? Alt, double?[]? Lat, double?[]? Lon,
    short?[]? Cadence, short?[]? Temp);

// Varv som klockan registrerade (manuellt eller automatiskt, t.ex. varje km)
public record ActivityLap(
    short LapIndex, int StartS, int DurationS, int? MovingS, double DistanceM, short? AvgHr, short? MaxHr,
    float? AscentM, float? DescentM, short? AvgCadence);

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

        // Ett eget pass med tidsserier för passsidan. Någon annans pass ger 404, som ett som inte finns.
        // shortcut: skickar alla mätpunkter (ett sexttimmarspass ≈ 1–2 MB JSON); glesa ut eller komprimera när det märks.
        activities.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                select a.id, a.sport, a.title, a.started_at, a.duration_s, a.moving_s, a.distance_m, a.avg_hr, a.max_hr,
                       a.avg_speed_mps, a.elevation_gain_m, a.device, s.t, s.dist, s.hr, s.speed, s.alt, s.lat, s.lon,
                       s.cadence, s.temp
                from activities a left join activity_streams s on s.activity_id = a.id
                where a.id = @id and a.user_id = @userId
                """, conn);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("userId", user.GetUserId());
            ActivityDetail detail;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound();

                // Läses för hand: Dapper/Npgsql klarar inte null-element i arrayer utan typen float?[] osv.
                T? Value<T>(int i) => reader.IsDBNull(i) ? default : reader.GetFieldValue<T>(i);
                var streams = reader.IsDBNull(12) ? null : new ActivityStreams(
                    reader.GetFieldValue<int[]>(12), Value<float?[]>(13), Value<short?[]>(14), Value<float?[]>(15),
                    Value<float?[]>(16), Value<double?[]>(17), Value<double?[]>(18), Value<short?[]>(19),
                    Value<short?[]>(20));
                detail = new ActivityDetail(
                    reader.GetGuid(0), reader.GetString(1), Value<string>(2),
                    System.DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc), reader.GetInt32(4),
                    Value<int?>(5), reader.GetDouble(6), Value<short?>(7), Value<short?>(8), Value<float?>(9),
                    Value<float?>(10), Value<string>(11), streams, []);
            }
            var laps = await conn.QueryAsync<ActivityLap>(
                """
                select lap_index, start_s, duration_s, moving_s, distance_m, avg_hr, max_hr, ascent_m, descent_m, avg_cadence
                from activity_laps where activity_id = @id order by lap_index
                """, new { id });
            return Results.Ok(detail with { Laps = laps });
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
                                            avg_hr, max_hr, avg_speed_mps, elevation_gain_m, route, device,
                                            raw_file_key, raw_file_sha256)
                    values (@userId, 'fit', @Sport, @StartedAt, @DurationS, @MovingS, @DistanceM,
                            @AvgHr, @MaxHr, @AvgSpeedMps, @ElevationGainM, @route::geography, @Device, @key, @sha256)
                    returning id
                    """,
                    new
                    {
                        userId, parsed.Sport, parsed.StartedAt, parsed.DurationS, parsed.MovingS, parsed.DistanceM,
                        parsed.AvgHr, parsed.MaxHr, parsed.AvgSpeedMps, parsed.ElevationGainM,
                        route = (object?)parsed.Route ?? DBNull.Value, parsed.Device, key, sha256,
                    }, tx);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Samma fil uppladdad samtidigt i en annan förfrågan
                return Results.Conflict(new ApiError("Det här passet är redan uppladdat.", "duplicateActivity"));
            }
            await conn.ExecuteAsync(
                """
                insert into activity_streams (activity_id, t, dist, hr, speed, alt, lat, lon, cadence, temp)
                values (@id, @T, @Dist, @Hr, @Speed, @Alt, @Lat, @Lon, @Cadence, @Temp)
                """,
                new
                {
                    id, parsed.T, parsed.Dist, parsed.Hr, parsed.Speed, parsed.Alt, parsed.Lat, parsed.Lon,
                    parsed.Cadence, parsed.Temp,
                }, tx);
            await conn.ExecuteAsync(
                """
                insert into activity_laps (activity_id, lap_index, start_s, duration_s, moving_s, distance_m, avg_hr, max_hr,
                                           ascent_m, descent_m, avg_cadence)
                values (@id, @LapIndex, @StartS, @DurationS, @MovingS, @DistanceM, @AvgHr, @MaxHr,
                        @AscentM, @DescentM, @AvgCadence)
                """,
                (parsed.Laps ?? []).Select((l, i) => new
                {
                    id, LapIndex = (short)i, l.StartS, l.DurationS, l.MovingS, l.DistanceM, l.AvgHr, l.MaxHr,
                    l.AscentM, l.DescentM, l.AvgCadence,
                }), tx);
            await BestEfforts.ReplaceAsync(conn, tx, id, userId, parsed.Sport, parsed.StartedAt, parsed.T, parsed.Dist);
            await tx.CommitAsync(cancellationToken);

            return Results.Created($"/api/activities/{id}", new ActivitySummary(
                id, parsed.Sport, null, parsed.StartedAt, parsed.DurationS, parsed.DistanceM, parsed.AvgHr, null));
        })
        // CSRF-token kontrolleras av filtret i AuthEndpoints (X-CSRF-TOKEN), inte av ramverkets formulärkontroll
        .DisableAntiforgery();

        activities.MapDelete("", async (NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            var userId = user.GetUserId();
            var owned = (await conn.QueryAsync<OwnedActivity>(
                "select id, raw_file_key from activities where user_id = @userId for update",
                new { userId }, tx)).ToArray();
            // Råfiler först; radera bara de pass som valdes, så en samtidig import behåller sin råfil.
            await store.DeleteAsync(owned.Select(a => a.RawFileKey).OfType<string>(), cancellationToken);
            await conn.ExecuteAsync("delete from activities where user_id = @userId and id = any(@ids)",
                new { userId, ids = owned.Select(a => a.Id).ToArray() }, tx);
            await tx.CommitAsync(cancellationToken);
            return Results.NoContent();
        });

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
