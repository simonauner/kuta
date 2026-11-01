using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class JobsTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Queue_allows_one_active_job_per_kind_hands_each_job_to_one_worker_and_retries_with_a_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await db.OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            foreach (var id in new[] { userId, otherId })
                await conn.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
            var job = (await JobQueue.EnqueueAsync(conn, userId, JobKinds.Recalculate, ct: ct))!;
            Assert.Null(await JobQueue.EnqueueAsync(conn, userId, JobKinds.Recalculate, ct: ct));
            Assert.NotNull(await JobQueue.EnqueueAsync(conn, otherId, JobKinds.Recalculate, ct: ct));
            Assert.Null(await JobQueue.GetAsync(conn, job.Id, otherId, ct));

            // Två samtidiga arbetare får aldrig samma jobb.
            await using var second = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var claimed = await conn.QuerySingleAsync<Guid>("""
                select id from jobs where user_id = @userId and status = 'queued' for update skip locked
                """, new { userId }, tx);
            Assert.Equal(job.Id, claimed);
            Assert.Null(await JobQueue.ClaimAsync(second, userId, ct));
            await tx.RollbackAsync(ct);

            var running = (await JobQueue.ClaimAsync(conn, userId, ct))!;
            Assert.Equal(("running", 1), (running.Status, running.Attempts));
            Assert.Null(await JobQueue.ClaimAsync(second, userId, ct));
            Assert.True(await JobQueue.HeartbeatAsync(conn, job.Id, """{"processed":1}""", ct));

            // Ett utgånget lås (arbetaren dog) gör jobbet körbart igen.
            await conn.ExecuteAsync("update jobs set locked_until = now() - interval '1 second' where id = @Id", job);
            Assert.Equal(2, (await JobQueue.ClaimAsync(second, userId, ct))!.Attempts);

            // Tillfälliga fel försöks igen senare; efter sista försöket är jobbet misslyckat med en stabil kod.
            Assert.False(await JobQueue.FailAsync(conn, job.Id, "jobFailed", retry: true, ct));
            var retry = (await JobQueue.GetAsync(conn, job.Id, userId, ct))!;
            Assert.Equal(("queued", (string?)null), (retry.Status, retry.ErrorCode));
            Assert.True(await conn.ExecuteScalarAsync<bool>("select run_after > now() from jobs where id = @Id", job));
            Assert.Null(await JobQueue.ClaimAsync(conn, userId, ct));
            await conn.ExecuteAsync("update jobs set run_after = now(), attempts = @max - 1 where id = @Id", new { job.Id, max = JobQueue.MaxAttempts });
            await JobQueue.ClaimAsync(conn, userId, ct);
            Assert.True(await JobQueue.FailAsync(conn, job.Id, "jobFailed", retry: true, ct));
            var failed = (await JobQueue.GetAsync(conn, job.Id, userId, ct))!;
            Assert.Equal(("failed", "jobFailed"), (failed.Status, failed.ErrorCode));
            Assert.NotNull(failed.FinishedAt);

            // Ett avslutat jobb frigör platsen; gamla avslutade jobb rensas.
            Assert.NotNull(await JobQueue.EnqueueAsync(conn, userId, JobKinds.Recalculate, ct: ct));
            await conn.ExecuteAsync("update jobs set finished_at = now() - @age where id = @Id",
                new { job.Id, age = JobQueue.Retention + TimeSpan.FromDays(1) });
            await JobQueue.PruneAsync(conn, ct);
            Assert.Null(await JobQueue.GetAsync(conn, job.Id, userId, ct));

            // Raderat konto: jobben kaskadraderas och en pågående körning märker det vid nästa förlopp.
            var active = (await JobQueue.ClaimAsync(conn, userId, ct))!;
            await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId });
            Assert.False(await JobQueue.HeartbeatAsync(conn, active.Id, null, ct));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@userId, @otherId)", new { userId, otherId });
        }
    }

    [Fact]
    public async Task Recalculation_replaces_derived_values_for_the_owners_activities_and_resumes_after_the_saved_activity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            foreach (var id in new[] { userId, otherId })
                await conn.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
            // Fyra pass i 3 m/s utan beräknade värden, varav ett hos en annan användare och ett manuellt utan tidsserier.
            var ids = new List<Guid>();
            foreach (var owner in new[] { userId, userId, userId, otherId })
            {
                var id = await conn.ExecuteScalarAsync<Guid>("""
                    insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                    values (@owner, 'fit', 'running', now(), 1000, 3000) returning id
                    """, new { owner });
                await conn.ExecuteAsync("""
                    insert into activity_streams (activity_id, t, dist)
                    select @id, array_agg(s order by s), array_agg((s * 3)::real order by s) from generate_series(0, 1000) s
                    """, new { id });
                ids.Add(id);
            }
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                values (@userId, 'manual', 'running', now(), 1000, 3000)
                """, new { userId });
            var own = ids.Take(3).OrderBy(id => id).ToList();

            var job = (await JobQueue.EnqueueAsync(conn, userId, JobKinds.Recalculate, ct: ct))!;
            await conn.ExecuteAsync("update jobs set progress = @progress::jsonb where id = @Id", new
            {
                job.Id,
                progress = JobQueue.Serialize(new RecalculationProgress(1, 3, own[0])),
            });
            await JobWorker.RunAllAsync(factory.Services, userId, ct);

            var done = (await JobQueue.GetAsync(conn, job.Id, userId, ct))!;
            Assert.Equal("succeeded", done.Status);
            Assert.Equal(3, JobQueue.Deserialize<RecalculationResult>(done.Result)!.Recalculated);
            var intensities = (await conn.QueryAsync<(Guid Id, double? Intensity, int Efforts)>("""
                select a.id, a.run_intensity, (select count(*)::int from best_efforts b where b.activity_id = a.id)
                from activities a where a.id = any(@ids)
                """, new { ids })).ToDictionary(r => r.Id);
            // Det sparade förloppet hoppar över det första passet; den andra användarens pass rörs inte.
            Assert.Equal((null, 0), (intensities[own[0]].Intensity, intensities[own[0]].Efforts));
            Assert.All(own.Skip(1), id => Assert.Equal((9000d, 2), (Math.Round(intensities[id].Intensity!.Value), intensities[id].Efforts)));
            Assert.Equal((null, 0), (intensities[ids[3]].Intensity, intensities[ids[3]].Efforts));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@userId, @otherId)", new { userId, otherId });
        }
    }

    [Fact]
    public async Task Recalculation_and_jobs_are_requested_and_read_by_their_owner_over_http()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var owner = factory.CreateClient();
        var other = factory.CreateClient();
        var anonymous = factory.CreateClient();
        var ownerEmail = $"jobs-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"jobs-other-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, ownerEmail, "Synthetic runner", ct);
            await TestSession.CreateRunnerAsync(factory, other, conn, otherEmail, "Synthetic runner", ct);
            using var first = await owner.PostAsync("/api/activities/recalculate", null, ct);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            var job = (await first.Content.ReadFromJsonAsync<JobResponse>(ct))!;
            Assert.Equal(("recalculate", "queued"), (job.Kind, job.Status));
            // Ett aktivt jobb återanvänds i stället för att köa ett till.
            using var again = await owner.PostAsync("/api/activities/recalculate", null, ct);
            Assert.Equal(job.Id, (await again.Content.ReadFromJsonAsync<JobResponse>(ct))!.Id);

            var latest = (await owner.GetFromJsonAsync<JobResponse[]>("/api/jobs", ct))!;
            Assert.Equal(job.Id, Assert.Single(latest).Id);
            Assert.Empty((await other.GetFromJsonAsync<JobResponse[]>("/api/jobs", ct))!);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/jobs/{job.Id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/jobs/{job.Id}", ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@ownerEmail, @otherEmail)", new { ownerEmail, otherEmail });
        }
    }
}
