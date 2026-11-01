using System.Security.Claims;
using Npgsql;

public static class JobEndpoints
{
    public static void MapJobs(this RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs").RequireAuthorization();
        // Senaste jobbet av varje slag, även avslutade, så att webben kan visa förlopp och resultat efter omladdning.
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

        // Köar omberäkning av alla egna pass; ett redan aktivt jobb returneras i stället för ett nytt.
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
