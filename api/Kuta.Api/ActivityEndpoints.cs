using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Dynastream.Fit;
using Npgsql;

// Typade records, inte dynamic: Dapper-rader serialiseras som dictionary med snake_case-nycklar.
public record ActivitySummary(
    Guid Id, string Sport, string? Title, System.DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, string[] Tags, Guid[] EquipmentIds);

// Ett pass med tidsserier (en array per kanal, index = mätpunkt; null där klockan inte mätte).
public record ActivityDetail(
    Guid Id, string Sport, string? Title, System.DateTime StartedAt, int DurationS, int? MovingS, double DistanceM,
    short? AvgHr, short? MaxHr, float? AvgSpeedMps, float? ElevationGainM, string? Device, ActivityStreams? Streams,
    IEnumerable<ActivityLap> Laps, string[] Tags, Guid[] EquipmentIds, ActivityPart[] Parts, bool PartsEdited, bool CanSplit, string? Description, ActivityPart[] OriginalParts, string OriginalSport, Guid UserId, string DisplayName, string Privacy);

// Det som formuläret förifylls med från en FIT-fil
public record ActivityPreview(
    string Sport, System.DateTime StartedAt, int DurationS, int? MovingS, double DistanceM, short? AvgHr, short? MaxHr,
    float? ElevationGainM, string? Device, ActivityStreams Streams, ActivityPart[] Parts, bool CanSplit);

public record ActivityStreams(
    int[] T, float?[]? Dist, short?[]? Hr, float?[]? Speed, float?[]? Alt, double?[]? Lat, double?[]? Lon,
    short?[]? Cadence, short?[]? Temp);

// Varv som klockan registrerade (manuellt eller automatiskt, t.ex. varje km)
public record ActivityLap(
    short LapIndex, int StartS, int DurationS, int? MovingS, double DistanceM, short? AvgHr, short? MaxHr,
    float? AscentM, float? DescentM, short? AvgCadence);

public record ActivityUpdateRequest(string? Title, string? Description, string[] Tags, Guid[] EquipmentIds, MultisportRequest? Multisport = null, bool RestoreParts = false, string? Privacy = null);

public static class ActivityEndpoints
{
    private const int MaxFitFileBytes = 20 * 1024 * 1024;

    public static void MapActivities(this RouteGroupBuilder api)
    {
        api.MapGet("/activity-tags", () => Results.Ok(ActivityInput.Tags)).RequireAuthorization();

        var activities = api.MapGroup("/activities").RequireAuthorization();
        activities.MapMultisport();

        // Inloggad användares pass, 25 åt gången med stabila rubriker för hela perioderna.
        activities.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user, string? timeZone, string? tag, string? sport,
            DateTimeOffset? beforeStartedAt, Guid? beforeId, int? year, CancellationToken cancellationToken) =>
        {
            if (beforeStartedAt.HasValue != beforeId.HasValue)
                return Results.BadRequest(new ApiError("Sidmarkören är ogiltig. Ladda om sidan.", "invalidActivityPage"));
            if (year is < 1 or > 9998)
                return Results.BadRequest(new ApiError("Ange ett år mellan 1 och 9998.", "invalidActivityYear"));
            if (ActivityInput.ValidateTags(tag is null ? null : [tag]) is { } invalid)
                return Results.BadRequest(invalid);
            if (sport is { Length: 0 or > 100 })
                return Results.BadRequest(new ApiError("Kontrollera sport, datum, tid och distans.", "invalidActivity"));
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, cancellationToken))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await ActivityList.ReadAsync(conn, user.GetUserId(), timeZone,
                beforeStartedAt, beforeId, cancellationToken, tag, year, sport));
        });

        activities.MapGet("/years", async (NpgsqlDataSource db, ClaimsPrincipal user, string? timeZone,
            CancellationToken cancellationToken) =>
        {
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, cancellationToken))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await ActivityList.ReadYearsAsync(conn, user.GetUserId(), timeZone, cancellationToken));
        });

        activities.MapGet("/sport-groups", () => Results.Ok(SportGroups.All));
        activities.MapGet("/statistics", async (NpgsqlDataSource db, ClaimsPrincipal user, int year,
            string? timeZone, string? sportGroup, CancellationToken cancellationToken) =>
        {
            if (year is < 1 or > 9998)
                return Results.BadRequest(new ApiError("Ange ett år mellan 1 och 9998.", "invalidActivityYear"));
            if (sportGroup is not null && !SportGroups.IsValid(sportGroup))
                return Results.BadRequest(new ApiError("Välj en giltig träningsform.", "invalidSportGroup"));
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, cancellationToken))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await ActivityStatistics.ReadAsync(conn, user.GetUserId(), year, timeZone,
                sportGroup, cancellationToken));
        });

        activities.MapGet("/statistics/days", async (NpgsqlDataSource db, ClaimsPrincipal user, string? timeZone,
            string? sportGroup, CancellationToken cancellationToken) =>
        {
            if (sportGroup is not null && !SportGroups.IsValid(sportGroup))
                return Results.BadRequest(new ApiError("Välj en giltig träningsform.", "invalidSportGroup"));
            timeZone ??= "UTC";
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            if (!await ActivityList.IsValidTimeZoneAsync(conn, timeZone, cancellationToken))
                return Results.BadRequest(new ApiError("Tidszonen är ogiltig. Kontrollera enhetens inställningar.", "invalidTimeZone"));
            return Results.Ok(await DailyTotals.ReadAsync(conn, user.GetUserId(), timeZone, sportGroup, cancellationToken));
        });

        // Shared activities use the same read rule as the feed; private activities still return 404.
        // shortcut: skickar alla mätpunkter (ett sexttimmarspass ≈ 1–2 MB JSON); glesa ut eller komprimera när det märks.
        activities.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var detail = await ReadDetailAsync(conn, user.GetUserId(), id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        // Förhandsvisning för formuläret "Skapa ny aktivitet": tolkar filen utan att spara något.
        // Pulsen följer samtycket även här, så att formuläret visar det som faktiskt kommer att sparas.
        activities.MapPost("/preview", async (IFormFile? file, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var (fit, error) = await ReadFitAsync(file, conn, user.GetUserId(), cancellationToken);
            if (fit is null) return error!;
            var p = fit.Parsed;
            return Results.Ok(new ActivityPreview(
                p.Sport, p.StartedAt, p.DurationS, p.MovingS, p.DistanceM, p.AvgHr, p.MaxHr, p.ElevationGainM, p.Device,
                new ActivityStreams(p.T, p.Dist, p.Hr, p.Speed, p.Alt, p.Lat, p.Lon, p.Cadence, p.Temp),
                p.Parts ?? [], Multisport.CanSplit(p.T, p.Dist)));
        })
        .DisableAntiforgery();

        // Manuellt pass utan fil: bara sammanfattning, inga tidsserier eller rekord
        activities.MapPost("/manual", async (ManualActivityRequest req, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (ActivityInput.Validate(req, DateTimeOffset.UtcNow) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            ActivitySummary? activity;
            try { activity = await ActivityStorage.SaveManualAsync(conn, user.GetUserId(), req, cancellationToken); }
            catch (InvalidEquipmentException) { return Results.BadRequest(EquipmentEndpoints.InvalidEquipment); }
            return activity is null
                ? Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"))
                : Results.Created($"/api/activities/{activity.Id}", activity);
        });

        // Uppladdning av en FIT-fil: tolka den, spara råfilen (bara med pulssamtycke) och skriv passet med tidsserier
        // i en transaktion. Samma fil igen ger 409 (raw_file_sha256 är unik per användare).
        // Titel och sport kan ändras i formuläret; mätvärdena kommer alltid från filen.
        activities.MapPost("", async (IFormFile? file, [FromForm] string? title, [FromForm] string? sport, IFormCollection form,
            NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            var tags = form["tags"].Select(tag => tag!).ToArray();
            string? description = form["description"];
            var equipmentIds = new List<Guid>();
            foreach (var value in form["equipmentIds"])
            {
                if (!Guid.TryParse(value, out var equipmentId)) return Results.BadRequest(EquipmentEndpoints.InvalidEquipment);
                equipmentIds.Add(equipmentId);
            }
            if ((ActivityInput.ValidateTitle(title) ?? ActivityInput.ValidateDescription(description) ?? ActivityInput.ValidateSport(sport) ?? ActivityInput.ValidateTags(tags)) is { } invalid)
                return Results.BadRequest(invalid);
            var userId = user.GetUserId();
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var (fit, error) = await ReadFitAsync(file, conn, userId, cancellationToken);
            if (fit is null) return error!;
            var (bytes, sha256, parsed, hrConsent) = fit;
            if (sport is not null && parsed.Parts is not { Length: > 0 }) parsed = parsed with { Sport = sport };
            MultisportRequest? split = null;
            if (form.ContainsKey("parts"))
            {
                try { split = JsonSerializer.Deserialize<MultisportRequest>(form["parts"].ToString(), JsonSerializerOptions.Web); }
                catch (JsonException) { return Results.BadRequest(Multisport.InvalidParts); }
                if (split is null || Multisport.Build(split, parsed.T, parsed.Dist) is null)
                    return Results.BadRequest(Multisport.InvalidParts);
            }
            title = ActivityInput.NormalizeTitle(title);

            Guid? id;
            try
            {
                id = await ActivityStorage.SaveAsync(conn, store, userId, parsed, "fit", title, null, bytes, ".fit",
                    cancellationToken, tags, equipmentIds.ToArray(), description, split);
            }
            catch (InvalidEquipmentException)
            {
                return Results.BadRequest(EquipmentEndpoints.InvalidEquipment);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation && e.TableName == "activity_equipment")
            {
                // Composite foreign keys reject unknown equipment and equipment owned by another user.
                return Results.BadRequest(EquipmentEndpoints.InvalidEquipment);
            }
            if (id is null) return Results.Conflict(new ApiError("Det här passet är redan uppladdat.", "duplicateActivity"));

            return Results.Created($"/api/activities/{id}", new ActivitySummary(
                id.Value, split?.Kind ?? parsed.Sport, title, parsed.StartedAt, parsed.DurationS, split is null ? parsed.DistanceM : Multisport.Build(split, parsed.T, parsed.Dist)!.Sum(p => p.DistanceM), parsed.AvgHr, ActivityInput.NormalizeTags(tags), equipmentIds.Distinct().ToArray()));
        })
        // CSRF-token kontrolleras av filtret i AuthEndpoints (X-CSRF-TOKEN), inte av ramverkets formulärkontroll
        .DisableAntiforgery();

        activities.MapPut("/{id:guid}", async (Guid id, ActivityUpdateRequest req,
            NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (req.Privacy is not null && !ActivityInput.IsValidPrivacy(req.Privacy))
                return Results.BadRequest(new ApiError("Välj giltig synlighet.", "invalidPrivacy"));
            if (req.Tags is null) return Results.BadRequest(new ApiError("Välj giltiga taggar.", "invalidTags"));
            if ((ActivityInput.ValidateTitle(req.Title) ?? ActivityInput.ValidateDescription(req.Description) ?? ActivityInput.ValidateTags(req.Tags)) is { } invalid) return Results.BadRequest(invalid);
            if (req.EquipmentIds is null) return Results.BadRequest(EquipmentEndpoints.InvalidEquipment);
            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var userId = user.GetUserId();
            var owned = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "select id from activities where id = @id and user_id = @userId for update", new { id, userId }, tx);
            if (owned is null) return Results.NotFound();
            if (req.RestoreParts && req.Multisport is not null) return Results.BadRequest(Multisport.InvalidParts);
            if (req.RestoreParts || req.Multisport is not null)
            {
                var result = await Multisport.ApplyAsync(conn, tx, userId, id, req.Multisport);
                if (result != 204) return Results.BadRequest(Multisport.InvalidParts);
            }
            if (!await EquipmentEndpoints.OwnsAsync(conn, tx, userId, req.EquipmentIds, allowRetired: true)) return Results.BadRequest(EquipmentEndpoints.InvalidEquipment);
            await conn.ExecuteAsync("delete from activity_equipment where activity_id = @id and user_id = @userId",
                new { id, userId }, tx);
            await EquipmentEndpoints.AttachAsync(conn, tx, id, userId, req.EquipmentIds);
            await conn.ExecuteAsync("update activities set title = @title, description = @description, tags = @tags, privacy = coalesce(@Privacy, privacy) where id = @id and user_id = @userId",
                new { id, userId, req.Privacy, title = ActivityInput.NormalizeTitle(req.Title), description = ActivityInput.NormalizeDescription(req.Description), tags = ActivityInput.NormalizeTags(req.Tags) }, tx);
            await tx.CommitAsync();
            return Results.NoContent();
        });
        activities.MapStravaImport();

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
            return await ActivityStorage.DeleteAsync(conn, store, user.GetUserId(), id, cancellationToken)
                ? Results.NoContent() : Results.NotFound();
        });
    }

    public static async Task<ActivityDetail?> ReadDetailAsync(NpgsqlConnection conn, Guid userId, Guid id,
        CancellationToken cancellationToken, NpgsqlTransaction? tx = null)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select a.id, a.sport, a.title, a.started_at, a.duration_s, a.moving_s, coalesce((select sum(p.distance_m) from activity_parts p where p.activity_id = a.id and p.original = not a.parts_edited), a.distance_m), a.avg_hr, a.max_hr,
                   a.avg_speed_mps, a.elevation_gain_m, a.device, s.t, s.dist, s.hr, s.speed, s.alt, s.lat, s.lon,
                   s.cadence, s.temp, a.tags,
                   array(select equipment_id from activity_equipment where activity_id = a.id order by equipment_id) as equipment_ids, a.parts_edited, a.description, coalesce(a.original_sport, a.sport), a.user_id, u.display_name, a.privacy
            from activities a join users u on u.id = a.user_id left join activity_streams s on s.activity_id = a.id
            where a.id = @id and {SocialEndpoints.CanRead}
            """, conn, tx);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        ActivityDetail detail;
        Guid ownerId;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;

            // Läses för hand: Dapper/Npgsql klarar inte null-element i arrayer utan typen float?[] osv.
            T? Value<T>(int i) => reader.IsDBNull(i) ? default : reader.GetFieldValue<T>(i);
            var streams = reader.IsDBNull(12) ? null : new ActivityStreams(
                reader.GetFieldValue<int[]>(12), Value<float?[]>(13), Value<short?[]>(14), Value<float?[]>(15),
                Value<float?[]>(16), Value<double?[]>(17), Value<double?[]>(18), Value<short?[]>(19),
                Value<short?[]>(20));
            ownerId = reader.GetGuid(26);
            detail = new ActivityDetail(
                reader.GetGuid(0), reader.GetString(1), Value<string>(2),
                System.DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc), reader.GetInt32(4),
                Value<int?>(5), reader.GetDouble(6), Value<short?>(7), Value<short?>(8), Value<float?>(9),
                Value<float?>(10), Value<string>(11), streams, [], reader.GetFieldValue<string[]>(21), reader.GetFieldValue<Guid[]>(22), [], reader.GetBoolean(23), streams is not null && Multisport.CanSplit(streams.T, streams.Dist), Value<string>(24), [], reader.GetString(25), ownerId, reader.GetString(27), reader.GetString(28));
        }
        var places = ownerId == userId ? [] : await PrivacyPlaces.LoadAsync(conn, ownerId, cancellationToken, tx);
        detail = detail with { Streams = RoutePrivacy.ForViewer(detail.Streams, ownerId, userId, places) };
        var laps = await conn.QueryAsync<ActivityLap>(
            """
            select lap_index, start_s, duration_s, moving_s, distance_m, avg_hr, max_hr, ascent_m, descent_m, avg_cadence
            from activity_laps where activity_id = @id order by lap_index
            """, new { id }, tx);
        detail = detail with { Laps = laps, Parts = await Multisport.LoadAsync(conn, id, tx), OriginalParts = await Multisport.LoadAsync(conn, id, tx, original: true) };
        // Full GPS streams and heart rate remain owner-only; feed map previews have separate privacy clipping.
        if (ownerId != userId)
            detail = detail with { AvgHr = null, MaxHr = null, Device = null, EquipmentIds = [],
                Streams = detail.Streams is { } streams ? streams with { Hr = null, Lat = null, Lon = null } : null,
                Laps = detail.Laps.Select(l => l with { AvgHr = null, MaxHr = null }).ToArray() };
        return detail;
    }

    private record OwnedActivity(Guid Id, string? RawFileKey);

    // En tolkad FIT-fil; utan pulssamtycke är pulsen redan borttagen
    private record FitUpload(byte[] Bytes, byte[] Sha256, ParsedActivity Parsed, bool HrConsent);

    private static async Task<(FitUpload? Fit, IResult? Error)> ReadFitAsync(IFormFile? file, NpgsqlConnection conn,
        Guid userId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return (null, Results.BadRequest(new ApiError("Välj en FIT-fil att ladda upp.", "fileRequired")));
        if (file.Length > MaxFitFileBytes)
            return (null, Results.BadRequest(new ApiError("Filen är för stor (högst 20 MB).", "fileTooLarge")));

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var sha256 = SHA256.HashData(bytes);

        var profile = await MeEndpoints.LoadProfileAsync(conn, userId);
        if (profile is null) return (null, Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired")));
        if (await conn.ExecuteScalarAsync<bool>(
                "select exists (select 1 from activities where user_id = @userId and raw_file_sha256 = @sha256)",
                new { userId, sha256 }))
            return (null, Results.Conflict(new ApiError("Det här passet är redan uppladdat.", "duplicateActivity")));

        ParsedActivity? parsed;
        try { parsed = FitImport.Parse(bytes); }
        catch (FitException) { parsed = null; }
        if (parsed is null)
            return (null, Results.BadRequest(new ApiError("Filen kunde inte läsas som ett pass från en FIT-fil.", "invalidFit")));
        return (new FitUpload(bytes, sha256, profile.HrConsent ? parsed : parsed.WithoutHeartRate(), profile.HrConsent), null);
    }
}
