using System.Diagnostics;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Dapper;
using Dynastream.Fit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Npgsql;

public record StravaImportResult(int Imported, int Duplicates, string[] FailedActivityIds, int EquipmentImported, int EquipmentLinked);

public record StravaImportProgress(int Processed, int Total, int Imported, int Duplicates, int Failed, StravaImportResult? Result = null);

public static class StravaImportEndpoint
{
    private const long MaxArchiveBytes = 500L * 1024 * 1024;

    public static void MapStravaImport(this RouteGroupBuilder activities)
    {
        activities.MapPost("/import/strava", async (IFormFile? file, NpgsqlDataSource db, RawFileStore store,
            ClaimsPrincipal user, HttpContext context, CancellationToken cancellationToken) =>
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
                using var csv = new MemoryStream(StravaImport.ReadFile(manifest));
                rows = StravaImport.ReadManifest(csv);
                using var shoeCsv = entries.TryGetValue("shoes.csv", out var shoeEntry)
                    ? new MemoryStream(StravaImport.ReadFile(shoeEntry)) : null;
                shoes = StravaImport.ReadShoes(shoeCsv, rows);
            }
            catch (Exception e) when (IsInvalidFile(e) || e is ArgumentException)
            {
                archive?.Dispose();
                return Results.BadRequest(new ApiError("ZIP-filen innehåller inte en giltig Strava-export.", "invalidStravaArchive"));
            }

            using (archive)
            {
                context.Response.ContentType = "application/x-ndjson";
                context.Response.Headers.CacheControl = "no-store";
                var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                async Task ReportAsync(StravaImportProgress progress)
                {
                    await context.Response.WriteAsync(JsonSerializer.Serialize(progress, jsonOptions) + "\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
                await ReportAsync(new(0, rows.Count, 0, 0, 0));
                var lastReport = Stopwatch.StartNew();
                var (equipment, equipmentImported) = await StravaEquipment.ImportAsync(conn, userId, shoes, cancellationToken);
                var equipmentLinked = 0;
                var imported = 0;
                var duplicates = 0;
                var failed = new List<string>();
                long expandedBytes = 0;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (lastReport.ElapsedMilliseconds >= 250)
                    {
                        await ReportAsync(new(imported + duplicates + failed.Count, rows.Count, imported, duplicates, failed.Count));
                        lastReport.Restart();
                    }
                    var equipmentId = row.GearId is { } gearId ? equipment[gearId] : (Guid?)null;
                    if (await conn.ExecuteScalarAsync<bool>(
                        "select exists(select 1 from activities where user_id = @userId and strava_activity_id = @Id)", new { userId, row.Id }))
                    {
                        if (equipmentId is { } existingShoe && await StravaEquipment.AttachMissingAsync(
                            conn, userId, existingShoe, row.Id, null, cancellationToken)) equipmentLinked++;
                        duplicates++;
                        continue;
                    }
                    if (expandedBytes > 1024L * 1024 * 1024)
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
                            bytes = StravaImport.ReadFile(entry);
                            expandedBytes += bytes.Length;
                            if (expandedBytes > 1024L * 1024 * 1024) throw new InvalidDataException("Archive expands too much.");
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
                            bytes, extension, cancellationToken, equipmentIds: equipmentId is { } shoeId ? [shoeId] : []);
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
                var result = new StravaImportResult(imported, duplicates, failed.ToArray(), equipmentImported, equipmentLinked);
                await ReportAsync(new(rows.Count, rows.Count, imported, duplicates, failed.Count, result));
                return Results.Empty;
            }
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxArchiveBytes + 1024 * 1024),
            new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxArchiveBytes });
    }

    private static bool IsInvalidFile(Exception e) => e is InvalidDataException or FormatException or OverflowException
        or XmlException or FitException or MalformedLineException or EndOfStreamException;
}
