using System.Security.Claims;
using Npgsql;

public static class JobEndpoints
{
    public static void MapJobs(this RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs").RequireAuthorization();
        // The latest job of each kind, including finished ones, so the web can show progress and results after a reload.
        jobs.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return Results.Ok((await JobQueue.LatestAsync(conn, user.GetUserId(), ct)).Select(JobResponse.From));
        });
        jobs.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await JobQueue.GetAsync(conn, id, user.GetUserId(), ct) is { } job
                ? Results.Ok(JobResponse.From(job)) : Results.NotFound();
        });

        // Queues recalculation of all own activities; an already active job is returned instead of a new one.
        api.MapPost("/activities/recalculate", async (NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var userId = user.GetUserId();
            if (await MeEndpoints.LoadProfileAsync(conn, userId) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            var job = await JobQueue.EnqueueAsync(conn, userId, JobKinds.Recalculate, ct: ct)
                ?? await JobQueue.ActiveAsync(conn, userId, JobKinds.Recalculate, ct);
            return job is null ? Results.Conflict() : Results.Accepted($"/api/jobs/{job.Id}", JobResponse.From(job));
        }).RequireAuthorization();
    }
}
