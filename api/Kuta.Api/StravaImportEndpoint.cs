using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Xml;
using Dapper;
using Dynastream.Fit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Npgsql;

public record StravaImportResult(int Imported, int Duplicates, string[] FailedActivityIds, int EquipmentImported, int EquipmentLinked);

public static class StravaImportEndpoint
{
    private const long MaxArchiveBytes = 500L * 1024 * 1024;

    public static void MapStravaImport(this RouteGroupBuilder activities)
    {
        activities.MapPost("/import/strava", async (IFormFile? file, NpgsqlDataSource db, RawFileStore store,
            ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0 || file.Length > MaxArchiveBytes)
                return Results.BadRequest(new ApiError("Välj en Strava-export som ZIP (högst 500 MB).", "stravaArchiveRequired"));
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var userId = user.GetUserId();
            if (await MeEndpoints.LoadProfileAsync(conn, userId) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));

            using var stream = file.OpenReadStream();
            ZipArchive? archive = null;
            List<StravaActivity> rows;
            List<StravaShoe> shoes;
            Dictionary<string, ZipArchiveEntry> entries;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read);
                if (archive.Entries.Count > 100000) throw new InvalidDataException("Too many entries.");
                entries = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
                if (!entries.TryGetValue("activities.csv", out var manifest)) throw new InvalidDataException("Missing activities.csv.");
                using var csv = new MemoryStream(StravaImport.ReadFile(manifest, new StravaImport.ExpansionBudget(StravaImport.MaxFileBytes), cancellationToken));
                rows = StravaImport.ReadManifest(csv);
                using var shoeCsv = entries.TryGetValue("shoes.csv", out var shoeEntry)
                    ? new MemoryStream(StravaImport.ReadFile(shoeEntry, new StravaImport.ExpansionBudget(StravaImport.MaxFileBytes), cancellationToken)) : null;
                shoes = StravaImport.ReadShoes(shoeCsv, rows);
            }
            catch (Exception e) when (IsInvalidFile(e) || e is ArgumentException)
            {
                archive?.Dispose();
                return Results.BadRequest(new ApiError("ZIP-filen innehåller inte en giltig Strava-export.", "invalidStravaArchive"));
            }

            using (archive)
            {
                var (equipment, equipmentImported) = await StravaEquipment.ImportAsync(conn, userId, shoes, cancellationToken);
                var equipmentLinked = 0;
                var imported = 0;
                var duplicates = 0;
                var failed = new List<string>();
                var budget = new StravaImport.ExpansionBudget(1024L * 1024 * 1024);
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var equipmentId = row.GearId is { } gearId ? equipment[gearId] : (Guid?)null;
                    if (await conn.ExecuteScalarAsync<bool>(
                        "select exists(select 1 from activities where user_id = @userId and strava_activity_id = @Id)", new { userId, row.Id }))
                    {
                        if (equipmentId is { } existingShoe && await StravaEquipment.AttachMissingAsync(
                            conn, userId, existingShoe, row.Id, null, cancellationToken)) equipmentLinked++;
                        duplicates++;
                        continue;
                    }
                    if (budget.Exhausted)
                    {
                        failed.Add(row.Id);
                        continue;
                    }
                    byte[]? bytes = null;
                    var parsed = row.Summary;
                    var extension = ".fit";
                    try
                    {
                        if (row.Filename.Length > 0)
                        {
                            if (!entries.TryGetValue(row.Filename, out var entry)) throw new InvalidDataException("Missing activity file.");
                            bytes = StravaImport.ReadFile(entry, budget, cancellationToken);
                            var filename = row.Filename.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? row.Filename[..^3] : row.Filename;
                            extension = Path.GetExtension(filename).ToLowerInvariant();
                            if (extension == ".fit")
                            {
                                var fit = FitImport.Parse(bytes) ?? throw new InvalidDataException("Invalid FIT.");
                                parsed = row.Summary with
                                {
                                    T = fit.T, Dist = fit.Dist, Hr = fit.Hr, Speed = fit.Speed, Alt = fit.Alt,
                                    Lat = fit.Lat, Lon = fit.Lon, Route = fit.Route,
                                    Device = fit.Device, Cadence = fit.Cadence, Temp = fit.Temp, Laps = fit.Laps,
                                };
                                // FIT-tiderna är relativa FIT-starten; CSV kan ha en annan starttid.
                                var offset = checked((int)Math.Round((fit.StartedAt - row.Summary.StartedAt).TotalSeconds));
                                parsed = parsed with
                                {
                                    T = fit.T.Select(t => checked(t + offset)).ToArray(),
                                    Laps = fit.Laps?.Select(l => l with { StartS = checked(l.StartS + offset) }).ToArray(),
                                };
                            }
                            else if (extension == ".gpx") parsed = StravaImport.ParseGpx(bytes, parsed);
                            else throw new InvalidDataException("Unsupported activity file.");
                        }
                    }
                    catch (Exception e) when (IsInvalidFile(e))
                    {
                        failed.Add(row.Id);
                        continue;
                    }
                    try
                    {
                        var id = await ActivityStorage.SaveAsync(conn, store, userId, parsed, "strava", row.Title, row.Id,
                            bytes, extension, cancellationToken, equipmentIds: equipmentId is { } shoeId ? [shoeId] : [], description: row.Description);
                        if (id is null)
                        {
                            duplicates++;
                            if (equipmentId is { } duplicateShoe && await StravaEquipment.AttachMissingAsync(
                                conn, userId, duplicateShoe, row.Id, bytes is null ? null : SHA256.HashData(bytes), cancellationToken))
                                equipmentLinked++;
                        }
                        else imported++;
                    }
                    catch (InvalidEquipmentException) { failed.Add(row.Id); }
                }
                return Results.Ok(new StravaImportResult(imported, duplicates, failed.ToArray(), equipmentImported, equipmentLinked));
            }
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxArchiveBytes + 1024 * 1024),
            new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxArchiveBytes });
    }

    private static bool IsInvalidFile(Exception e) => e is InvalidDataException or FormatException or OverflowException
        or XmlException or FitException or MalformedLineException or EndOfStreamException;
}
