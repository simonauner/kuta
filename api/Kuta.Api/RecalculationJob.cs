using Dapper;
using Npgsql;

public sealed record RecalculationProgress(int Processed, int Total, Guid? LastActivityId);
public sealed record RecalculationResult(int Recalculated);

// Räknar om rekord och löpintensitet för alla användarens pass med tidsserier, 50 åt gången i passets id-ordning.
// Förloppet sparar senaste passet, så ett avbrutet jobb fortsätter där det slutade.
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
                // Ett pass som raderats under tiden hoppas över.
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
