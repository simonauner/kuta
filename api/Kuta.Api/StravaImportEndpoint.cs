using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Xml;
using Dapper;
using Dynastream.Fit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Microsoft.VisualBasic.FileIO;
using Npgsql;

public record StravaImportResult(int Imported, int Duplicates, string[] FailedActivityIds, int EquipmentImported, int EquipmentLinked);

public record StravaImportProgress(int Processed, int Total, int Imported, int Duplicates, int Failed);

public static class StravaImportEndpoint
{
    private const long MaxArchiveBytes = 500L * 1024 * 1024;
    // Archives stay on the machine's disk until the worker (one job at a time) has run them: at most this many queued
    // or running imports plus two concurrent uploads, i.e. a few GB.
    private const int MaxQueuedImports = 4;
    private static readonly SemaphoreSlim Uploads = new(2);
    // A full archive must arrive within about 80 minutes.
    private const double MinUploadBytesPerSecond = 100 * 1024;

    // The upload saves the archive, checks the manifest and queues the import. The import itself is run by JobWorker.
    // The body is read as a stream only after the checks: with IFormFile the framework buffers the whole archive to disk before
    // the handler runs, so parallel calls filled the disk even though only one import at a time is queued.
    public static void MapStravaImport(this RouteGroupBuilder activities)
    {
        activities.MapPost("/import/strava", async (HttpRequest request, NpgsqlDataSource db, ImportFiles files,
            ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            var archiveRequired = Results.BadRequest(new ApiError("Välj en Strava-export som ZIP (högst 500 MB).", "stravaArchiveRequired"));
            var inProgress = Results.Conflict(new ApiError("En import pågår redan. Vänta tills den är klar.", "importInProgress"));
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
                || HeaderUtilities.RemoveQuotes(contentType.Boundary).Value is not { Length: > 0 } boundary)
                return archiveRequired;
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            var userId = user.GetUserId();
            if (await MeEndpoints.LoadProfileAsync(conn, userId) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            // Session lock per user for the whole upload; released explicitly since the connection is pooled.
            if (!await conn.ExecuteScalarAsync<bool>("select pg_try_advisory_lock(hashtext('strava-import:' || @userId))", new { userId }))
                return inProgress;
            var uploading = false;
            try
            {
                if (await JobQueue.ActiveAsync(conn, userId, JobKinds.StravaImport, cancellationToken) is not null)
                    return inProgress;
                if (await conn.ExecuteScalarAsync<int>("select count(*) from jobs where kind = @kind and status in ('queued', 'running')",
                        new { kind = JobKinds.StravaImport }) >= MaxQueuedImports || !(uploading = await Uploads.WaitAsync(0, cancellationToken)))
                    return Results.Json(new ApiError("Många importer pågår just nu. Försök igen om en stund.", "importBusy"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                // Only two uploads at a time: a client trickling the body at Kestrel's default 240 B/s would hold a slot for days.
                if (request.HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>() is { } rate)
                    rate.MinDataRate = new MinDataRate(MinUploadBytesPerSecond, TimeSpan.FromSeconds(10));
                var jobId = Guid.NewGuid();
                bool saved;
                try { saved = await SaveArchiveAsync(request.Body, boundary, files, jobId, cancellationToken); }
                catch (Microsoft.AspNetCore.Http.BadHttpRequestException e)
                {
                    // Too slow (408) or a broken body: the client's fault, so no error log
                    files.Delete(jobId);
                    return Results.Json(new ApiError("Uppladdningen gick för långsamt eller avbröts. Försök igen.", "uploadTooSlow"),
                        statusCode: e.StatusCode);
                }
                catch
                {
                    // An aborted upload leaves a partial file
                    files.Delete(jobId);
                    throw;
                }
                if (!saved)
                {
                    files.Delete(jobId);
                    return archiveRequired;
                }
                try
                {
                    await using var stream = System.IO.File.OpenRead(files.PathFor(jobId));
                    using var archive = StravaImportJob.Open(stream, cancellationToken);
                }
                catch (Exception e) when (StravaImportJob.IsInvalidFile(e) || e is ArgumentException)
                {
                    files.Delete(jobId);
                    return Results.BadRequest(new ApiError("ZIP-filen innehåller inte en giltig Strava-export.", "invalidStravaArchive"));
                }
                var job = await JobQueue.EnqueueAsync(conn, userId, JobKinds.StravaImport, jobId, cancellationToken);
                if (job is null)
                {
                    files.Delete(jobId);
                    return inProgress;
                }
                return Results.Accepted($"/api/jobs/{job.Id}", JobResponse.From(job));
            }
            finally
            {
                if (uploading) Uploads.Release();
                await conn.ExecuteAsync("select pg_advisory_unlock(hashtext('strava-import:' || @userId))", new { userId });
            }
        })
        // The CSRF token is checked by the filter in AuthEndpoints; the body is read as a stream instead of a form
        .DisableAntiforgery()
        .RequireRateLimiting("uploads")
        .WithMetadata(new RequestSizeLimitAttribute(MaxArchiveBytes + 1024 * 1024));
    }

    // Saves the "file" field from the multipart body. False when the field is missing, empty or larger than 500 MB.
    static async Task<bool> SaveArchiveAsync(Stream body, string boundary, ImportFiles files, Guid jobId, CancellationToken ct)
    {
        var reader = new MultipartReader(boundary, body);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !disposition.IsFileDisposition() || disposition.Name.Value?.Trim('"') != "file")
                continue;
            var limited = new LimitedStream(section.Body, MaxArchiveBytes);
            await files.SaveAsync(jobId, limited, ct);
            return limited.BytesRead > 0 && !limited.Exceeded;
        }
        return false;
    }

    // Stops reading just past the limit, so an oversized archive is never fully written to disk.
    sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        public long BytesRead { get; private set; }
        public bool Exceeded => BytesRead > limit;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (Exceeded) return 0;
            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, limit + 1 - BytesRead)], ct);
            BytesRead += read;
            return read;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public static class StravaImportJob
{
    public sealed record Archive(ZipArchive Zip, List<StravaActivity> Rows, List<StravaShoe> Shoes,
        Dictionary<string, ZipArchiveEntry> Entries) : IDisposable
    {
        public void Dispose() => Zip.Dispose();
    }

    const int MaxEntries = 100000;
    const long MaxCentralDirectoryBytes = 16 * 1024 * 1024;

    public static Archive Open(Stream stream, CancellationToken cancellationToken)
    {
        CheckCentralDirectory(stream);
        var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        try
        {
            var entries = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
            if (!entries.TryGetValue("activities.csv", out var manifest)) throw new InvalidDataException("Missing activities.csv.");
            using var csv = new MemoryStream(StravaImport.ReadFile(manifest, new StravaImport.ExpansionBudget(StravaImport.MaxFileBytes), cancellationToken));
            var rows = StravaImport.ReadManifest(csv);
            using var shoeCsv = entries.TryGetValue("shoes.csv", out var shoeEntry)
                ? new MemoryStream(StravaImport.ReadFile(shoeEntry, new StravaImport.ExpansionBudget(StravaImport.MaxFileBytes), cancellationToken)) : null;
            return new Archive(zip, rows, StravaImport.ReadShoes(shoeCsv, rows), entries);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    // The import is idempotent (duplicates via Strava ID or file hash), so an interrupted job reruns from the start.
    public static async Task<StravaImportResult> RunAsync(NpgsqlConnection conn, RawFileStore store, ImportFiles files,
        JobRow job, CancellationToken cancellationToken)
    {
        var userId = job.UserId;
        FileStream stream;
        try { stream = System.IO.File.OpenRead(files.PathFor(job.Id)); }
        catch (FileNotFoundException) { throw new JobFailedException("stravaImportInterrupted"); }
        await using var _ = stream;
        Archive archive;
        try { archive = Open(stream, cancellationToken); }
        catch (Exception e) when (IsInvalidFile(e) || e is ArgumentException) { throw new JobFailedException("invalidStravaArchive"); }
        using (archive)
        {
            var rows = archive.Rows;
            var lastReport = Stopwatch.StartNew();
            async Task ReportAsync(StravaImportProgress progress)
            {
                if (!await JobQueue.HeartbeatAsync(conn, job.Id, JobQueue.Serialize(progress), cancellationToken))
                    throw new JobFailedException("jobCancelled");
            }
            await ReportAsync(new(0, rows.Count, 0, 0, 0));
            var (equipment, equipmentImported) = await StravaEquipment.ImportAsync(conn, userId, archive.Shoes, cancellationToken);
            var equipmentLinked = 0;
            var imported = 0;
            var duplicates = 0;
            var failed = new List<string>();
            var budget = new StravaImport.ExpansionBudget(1024L * 1024 * 1024);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lastReport.ElapsedMilliseconds >= 1000)
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
                        if (!archive.Entries.TryGetValue(row.Filename, out var entry)) throw new InvalidDataException("Missing activity file.");
                        bytes = StravaImport.ReadFile(entry, budget, cancellationToken);
                        var filename = row.Filename.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? row.Filename[..^3] : row.Filename;
                        extension = Path.GetExtension(filename).ToLowerInvariant();
                        if (extension == ".fit")
                        {
                            var fit = FitImport.Parse(bytes) ?? throw new InvalidDataException("Invalid FIT.");
                            parsed = StravaImport.MergeFit(row.Summary, fit);
                        }
                        else if (extension == ".gpx") parsed = StravaImport.ParseGpx(bytes, parsed);
                        else throw new InvalidDataException("Unsupported activity file.");
                    }
                }
                // A broken activity should only fail itself: the SDK also throws exceptions other than the usual file errors, and
                // an error here would otherwise restart the whole import three times and block the worker for everyone.
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    failed.Add(row.Id);
                    continue;
                }
                try
                {
                    var id = await ActivityStorage.SaveAsync(conn, store, userId, parsed, "strava", row.Title, row.Id,
                        bytes, extension, cancellationToken, equipmentIds: equipmentId is { } shoeId ? [shoeId] : [], description: row.Description, tags: row.Tags);
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
            await ReportAsync(new(rows.Count, rows.Count, imported, duplicates, failed.Count));
            return new StravaImportResult(imported, duplicates, failed.ToArray(), equipmentImported, equipmentLinked);
        }
    }

    // zip.Entries builds an object per central directory record before anything can be counted, so a 500 MB archive of
    // tiny records exhausts memory. The record count and directory size are read from the end record (ZIP64 too) first.
    public static void CheckCentralDirectory(Stream stream)
    {
        var tail = new byte[(int)Math.Min(stream.Length, 20 + 22 + ushort.MaxValue)];
        stream.Seek(-tail.Length, SeekOrigin.End);
        stream.ReadExactly(tail);
        // Searched backwards like ZipArchive, which then parses the same record.
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != 0x06054b50) continue;
            ulong entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10));
            ulong size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12));
            if ((entries == ushort.MaxValue || size == uint.MaxValue)
                && i >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i - 20)) == 0x07064b50)
            {
                var zip64 = new byte[56];
                stream.Seek((long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(i - 12)), (ulong)stream.Length), SeekOrigin.Begin);
                stream.ReadExactly(zip64);
                if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != 0x06064b50) throw new InvalidDataException("Invalid ZIP64 end record.");
                entries = BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32));
                size = BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(40));
            }
            if (entries > MaxEntries || size > MaxCentralDirectoryBytes) throw new InvalidDataException("Too many entries.");
            break;
        }
        // Without an end record ZipArchive rejects the file itself.
        stream.Seek(0, SeekOrigin.Begin);
    }

    public static bool IsInvalidFile(Exception e) => e is InvalidDataException or FormatException or OverflowException
        or XmlException or FitException or MalformedLineException or EndOfStreamException;
}
