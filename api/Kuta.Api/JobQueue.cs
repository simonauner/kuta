using System.Text.Json;
using Dapper;
using Npgsql;

public static class JobKinds
{
    public const string StravaImport = "strava_import";
    public const string Recalculate = "recalculate";
    public const string GarminImport = "garmin_import";
    public static readonly string[] All = [StravaImport, Recalculate, GarminImport];
}

public sealed record JobRow(Guid Id, Guid UserId, string Kind, string Status, string? Progress, string? Result,
    string? ErrorCode, int Attempts, DateTime CreatedAt, DateTime? FinishedAt);

public sealed record JobResponse(Guid Id, string Kind, string Status, JsonElement? Progress, JsonElement? Result,
    string? ErrorCode, DateTime CreatedAt, DateTime? FinishedAt)
{
    public static JobResponse From(JobRow row) => new(row.Id, row.Kind, row.Status, Parse(row.Progress),
        Parse(row.Result), row.ErrorCode, row.CreatedAt, row.FinishedAt);

    static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}

// An aborted job that should not be retried; the code is shown to the user.
public sealed class JobFailedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// The queue is a plain table. A worker takes the next job with "for update skip locked" and holds it with a time-limited
// lock that is renewed while running. If the worker stops (restart, crash) the lock expires and the job is picked up again.
public static class JobQueue
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    const string Columns = "id, user_id, kind, status, progress::text, result::text, error_code, attempts, created_at, finished_at";

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T? Deserialize<T>(string? json) => json is null ? default : JsonSerializer.Deserialize<T>(json, Json);

    // Null when the user already has an active job of the same kind.
    public static async Task<JobRow?> EnqueueAsync(NpgsqlConnection conn, Guid userId, string kind, Guid? id = null,
        CancellationToken ct = default) =>
        await conn.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition($"""
            insert into jobs (id, user_id, kind) values (coalesce(@id, gen_random_uuid()), @userId, @kind)
            on conflict (user_id, kind) where status in ('queued', 'running') do nothing
            returning {Columns}
            """, new { id, userId, kind }, cancellationToken: ct));

    public static Task<JobRow?> ActiveAsync(NpgsqlConnection conn, Guid userId, string kind, CancellationToken ct = default) =>
        conn.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition($"""
            select {Columns} from jobs where user_id = @userId and kind = @kind and status in ('queued', 'running')
            """, new { userId, kind }, cancellationToken: ct));

    public static Task<JobRow?> GetAsync(NpgsqlConnection conn, Guid id, Guid userId, CancellationToken ct = default) =>
        conn.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition(
            $"select {Columns} from jobs where id = @id and user_id = @userId", new { id, userId }, cancellationToken: ct));

    // The latest job of each kind, so the web can show an ongoing import after a reload.
    public static async Task<IReadOnlyList<JobRow>> LatestAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct = default) =>
        (await conn.QueryAsync<JobRow>(new CommandDefinition($"""
            select distinct on (kind) {Columns} from jobs where user_id = @userId order by kind, created_at desc
            """, new { userId }, cancellationToken: ct))).ToList();

    // Takes the next runnable job: queued and due, or running with an expired lock. userId limits to one user (tests).
    public static Task<JobRow?> ClaimAsync(NpgsqlConnection conn, Guid? userId = null, CancellationToken ct = default) =>
        conn.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition($"""
            update jobs set status = 'running', attempts = attempts + 1, locked_until = now() + @lease
            where id = (
                select id from jobs
                where ((status = 'queued' and run_after <= now()) or (status = 'running' and locked_until < now()))
                    and (@userId::uuid is null or user_id = @userId)
                order by run_after, created_at
                for update skip locked limit 1)
            returning {Columns}
            """, new { lease = Lease, userId }, cancellationToken: ct));

    // Saves progress and renews the lock. False if the job is gone (account deleted) or has been taken over.
    public static async Task<bool> HeartbeatAsync(NpgsqlConnection conn, Guid id, string? progress, CancellationToken ct = default) =>
        await conn.ExecuteAsync(new CommandDefinition("""
            update jobs set progress = coalesce(@progress::jsonb, progress), locked_until = now() + @lease
            where id = @id and status = 'running'
            """, new { id, progress, lease = Lease }, cancellationToken: ct)) == 1;

    public static Task CompleteAsync(NpgsqlConnection conn, Guid id, string? result, CancellationToken ct = default) =>
        conn.ExecuteAsync(new CommandDefinition("""
            update jobs set status = 'succeeded', result = @result::jsonb, locked_until = null, finished_at = now()
            where id = @id
            """, new { id, result }, cancellationToken: ct));

    // Final failure, or a retry later with increasing delay until the attempts run out.
    public static async Task<bool> FailAsync(NpgsqlConnection conn, Guid id, string code, bool retry, CancellationToken ct = default) =>
        await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            update jobs set
                status = case when @retry and attempts < @maxAttempts then 'queued' else 'failed' end,
                run_after = now() + attempts * attempts * interval '30 seconds',
                error_code = case when @retry and attempts < @maxAttempts then null else @code end,
                locked_until = null,
                finished_at = case when @retry and attempts < @maxAttempts then null else now() end
            where id = @id
            returning status = 'failed'
            """, new { id, code, retry, maxAttempts = MaxAttempts }, cancellationToken: ct));

    // On shutdown: hand the job back immediately without counting the attempt.
    public static Task ReleaseAsync(NpgsqlConnection conn, Guid id) =>
        conn.ExecuteAsync("""
            update jobs set status = 'queued', attempts = greatest(attempts - 1, 0), locked_until = null
            where id = @id and status = 'running'
            """, new { id });

    public static Task<int> PruneAsync(NpgsqlConnection conn, CancellationToken ct = default) =>
        conn.ExecuteAsync(new CommandDefinition(
            "delete from jobs where finished_at < now() - @retention", new { retention = Retention }, cancellationToken: ct));
}
