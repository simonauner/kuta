using Dapper;
using Npgsql;

public sealed record RecalculationProgress(int Processed, int Total, Guid? LastActivityId);
public sealed record RecalculationResult(int Recalculated);

// Recalculates records and run intensity for all the user's activities with streams, 50 at a time in activity id order.
// The progress stores the latest activity, so an interrupted job continues where it left off.
public static class RecalculationJob
{
    const int BatchSize = 50;

    public static async Task<RecalculationResult> RunAsync(NpgsqlConnection conn, JobRow job, CancellationToken ct)
    {
        var state = JobQueue.Deserialize<RecalculationProgress>(job.Progress);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*)::int from activities a join activity_streams s on s.activity_id = a.id where a.user_id = @userId
            """, new { job.UserId }, cancellationToken: ct));
        var processed = state?.Processed ?? 0;
        var after = state?.LastActivityId;
        if (!await JobQueue.HeartbeatAsync(conn, job.Id,
                JobQueue.Serialize(new RecalculationProgress(processed, Math.Max(total, processed), after)), ct))
            throw new JobFailedException("jobCancelled");
        while (true)
        {
            var ids = (await conn.QueryAsync<Guid>(new CommandDefinition("""
                select a.id from activities a join activity_streams s on s.activity_id = a.id
                where a.user_id = @userId and (@after::uuid is null or a.id > @after)
                order by a.id limit @limit
                """, new { job.UserId, after, limit = BatchSize }, cancellationToken: ct))).ToList();
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                // An activity deleted in the meantime is skipped.
                await BestEfforts.RecalculateAsync(conn, id, job.UserId, ct);
                processed++;
            }
            after = ids[^1];
            if (!await JobQueue.HeartbeatAsync(conn, job.Id,
                    JobQueue.Serialize(new RecalculationProgress(processed, Math.Max(total, processed), after)), ct))
                throw new JobFailedException("jobCancelled");
        }
        return new RecalculationResult(processed);
    }
}
