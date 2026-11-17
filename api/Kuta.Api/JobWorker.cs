using Dapper;
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
    static readonly TimeSpan LockRetryDelay = TimeSpan.FromSeconds(30);

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
                    // Set first: a cleanup that keeps failing must wait an hour, not run instead of every job.
                    lastCleanup = DateTime.UtcNow;
                    await CleanupAsync(services, stoppingToken);
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
        // There is one worker for everyone, so a job must not wait long on a lock held by a request (a long data export
        // holds the profile). Npgsql resets the setting when the connection goes back to the pool.
        await conn.ExecuteAsync("set lock_timeout = '5s'");
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
                JobKinds.Routes => JobQueue.Serialize(await Routes.RunJobAsync(conn, job, ct)),
                JobKinds.Push => JobQueue.Serialize(await Push.RunJobAsync(conn,
                    services.GetRequiredService<WebPushSender>(), job, ct)),
                JobKinds.GarminImport => JobQueue.Serialize(await Garmin.RunImportAsync(conn,
                    services.GetRequiredService<RawFileStore>(), services.GetRequiredService<GarminClient>(),
                    services.GetRequiredService<IDataProtectionProvider>(), job, ct)),
                _ => throw new JobFailedException("jobFailed"),
            };
            await JobQueue.CompleteAsync(conn, job.Id, result, CancellationToken.None);
            if (job.Kind == JobKinds.StravaImport) files.Delete(job.Id);
            // Notices that arrived while the job ran got no job of their own (at most one active per user).
            if (job.Kind == JobKinds.GarminImport) await Garmin.EnqueueIfPendingAsync(conn, job.UserId, CancellationToken.None);
            if (job.Kind == JobKinds.Push) await Push.EnqueueIfPendingAsync(conn, job.UserId, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await JobQueue.ReleaseAsync(conn, job.Id);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // Busy, not broken: try again later without using up an attempt.
            await JobQueue.ReleaseAsync(conn, job.Id, LockRetryDelay);
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
