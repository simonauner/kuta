using Microsoft.AspNetCore.DataProtection;
using Npgsql;

// Archive files for queued Strava imports live on the machine's local disk while the job waits and runs. Production has a
// single machine; a restart removes the file and the job ends with a code asking for a new upload (the import skips
// duplicates). Multiple machines require moving the archive to object storage.
public sealed class ImportFiles(IConfiguration configuration)
{
    readonly string directory = configuration["Jobs:ImportDirectory"]
        ?? Path.Combine(Path.GetTempPath(), "kuta-imports");

    public string PathFor(Guid jobId)
    {
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{jobId:N}.zip");
    }

    public async Task SaveAsync(Guid jobId, Stream content, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(PathFor(jobId), options);
        await content.CopyToAsync(file, ct);
    }

    public void Delete(Guid jobId)
    {
        try { File.Delete(PathFor(jobId)); }
        catch (IOException) { }
    }

    // Files without an active job, for example after a deleted user or an interruption.
    public void DeleteExcept(IReadOnlySet<Guid> activeJobs)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.zip"))
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) || !activeJobs.Contains(id))
            {
                try { File.Delete(path); }
                catch (IOException) { }
            }
    }
}

// Runs queued jobs in the API process, one at a time. Jobs:Enabled = false disables the worker (tests run the jobs themselves).
public sealed class JobWorker(IServiceProvider services, IConfiguration configuration, ILoggerFactory loggers) : BackgroundService
{
    static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:Enabled", true)) return;
        var diagnostics = loggers.CreateLogger(PrivateLogProvider.Category);
        var lastCleanup = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastCleanup > CleanupInterval)
                {
                    await CleanupAsync(services, stoppingToken);
                    lastCleanup = DateTime.UtcNow;
                }
                if (!await RunNextAsync(services, null, stoppingToken, diagnostics))
                    await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception e)
            {
                // The database may be temporarily unavailable; try again after a pause.
                diagnostics.LogError(PrivateLogProvider.JobFailed, e, "");
                try { await Task.Delay(IdleDelay * 5, stoppingToken); }
                catch (OperationCanceledException) { }
            }
        }
    }

    public static async Task CleanupAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var conn = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await JobQueue.PruneAsync(conn, ct);
        await Garmin.MaintainAsync(conn, services.GetRequiredService<GarminClient>(),
            services.GetRequiredService<IDataProtectionProvider>(), ct);
        var active = await Dapper.SqlMapper.QueryAsync<Guid>(conn, new Dapper.CommandDefinition(
            "select id from jobs where kind = @kind and status in ('queued', 'running')",
            new { kind = JobKinds.StravaImport }, cancellationToken: ct));
        services.GetRequiredService<ImportFiles>().DeleteExcept(active.ToHashSet());
    }

    // Runs the next job. False when there was no job.
    public static async Task<bool> RunNextAsync(IServiceProvider services, Guid? userId, CancellationToken ct,
        ILogger? diagnostics = null)
    {
        var db = services.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await db.OpenConnectionAsync(ct);
        var job = await JobQueue.ClaimAsync(conn, userId, ct);
        if (job is null) return false;
        var files = services.GetRequiredService<ImportFiles>();
        if (job.Attempts > JobQueue.MaxAttempts)
        {
            await FinishFailedAsync(conn, files, job, "jobFailed", retry: false);
            return true;
        }
        try
        {
            var result = job.Kind switch
            {
                JobKinds.StravaImport => JobQueue.Serialize(await StravaImportJob.RunAsync(
                    conn, services.GetRequiredService<RawFileStore>(), files, job, ct)),
                JobKinds.Recalculate => JobQueue.Serialize(await RecalculationJob.RunAsync(conn, job, ct)),
                JobKinds.GarminImport => JobQueue.Serialize(await Garmin.RunImportAsync(conn,
                    services.GetRequiredService<RawFileStore>(), services.GetRequiredService<GarminClient>(),
                    services.GetRequiredService<IDataProtectionProvider>(), job, ct)),
                _ => throw new JobFailedException("jobFailed"),
            };
            await JobQueue.CompleteAsync(conn, job.Id, result, CancellationToken.None);
            if (job.Kind == JobKinds.StravaImport) files.Delete(job.Id);
            // Aviseringar som kom medan jobbet körde fick inget eget jobb (högst ett aktivt per användare).
            if (job.Kind == JobKinds.GarminImport) await Garmin.EnqueueIfPendingAsync(conn, job.UserId, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await JobQueue.ReleaseAsync(conn, job.Id);
        }
        catch (JobFailedException e)
        {
            await FinishFailedAsync(conn, files, job, e.Code, retry: false);
        }
        catch (Exception e)
        {
            diagnostics?.LogError(PrivateLogProvider.JobFailed, e, "");
            await FinishFailedAsync(conn, files, job, "jobFailed", retry: true);
        }
        return true;
    }

    static async Task FinishFailedAsync(NpgsqlConnection conn, ImportFiles files, JobRow job, string code, bool retry)
    {
        var final = await JobQueue.FailAsync(conn, job.Id, code, retry, CancellationToken.None);
        if (final && job.Kind == JobKinds.StravaImport) files.Delete(job.Id);
    }

    // Runs the user's jobs until the queue is empty (tests).
    public static async Task RunAllAsync(IServiceProvider services, Guid userId, CancellationToken ct)
    {
        while (await RunNextAsync(services, userId, ct)) { }
    }
}
