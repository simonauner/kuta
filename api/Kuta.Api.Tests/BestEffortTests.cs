using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class BestEffortTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Import_recalculation_records_and_deletion_preserve_ownership()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            using var owner = await CreateRunner(conn, ownerId, ct);
            using var other = await CreateRunner(conn, otherId, ct);
            using var anonymous = factory.CreateClient();
            var start = new DateTime(2025, 12, 31, 23, 59, 50, DateTimeKind.Utc);
            var first = await Upload(owner, start, ct);
            var second = await Upload(owner, start.AddYears(1), ct);
            var otherRun = await Upload(other, start.AddYears(1), ct);
            await conn.ExecuteAsync("update activities set tags = ARRAY['race'] where id = @id", new { id = second.Id });
            var raceRecords = await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records?tag=race&year=2026", ct);
            Assert.Equal(3, raceRecords!.Length);
            Assert.All(raceRecords, effort => Assert.Equal(second.Id, effort.ActivityId));
            Assert.Empty((await other.GetFromJsonAsync<StoredBestEffort[]>("/api/records?tag=race", ct))!);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/records?tag=unknown", ct)).StatusCode);

            var trend = (await owner.GetFromJsonAsync<RecordTrend>("/api/records/trend?distanceM=5000&timeZone=Europe/Stockholm", ct))!;
            Assert.Equal(new[] { (2026, 1), (2027, 1) }, trend.Months.Select(m => (m.Year, m.Month)));
            Assert.Equal(first.Id, Assert.Single(trend.Progression).ActivityId);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/records/trend?distanceM=3000", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await owner.GetAsync("/api/records/trend?distanceM=5000&timeZone=Nowhere", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/me/threshold-speed",
                new { thresholdSpeedMS = 12 }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync("/api/me/threshold-speed",
                new { thresholdSpeedMS = 4 }, ct)).StatusCode);
            var load = (await owner.GetFromJsonAsync<TrainingLoadSummary>("/api/training-load?timeZone=Europe/Stockholm", ct))!;
            Assert.Equal(("manual", 4d), (load.ThresholdSource, load.ThresholdSpeedMS!.Value));
            // Historiken börjar vid nyår, så svaret är kapat till de senaste dagarna och innehåller passets belastning.
            Assert.Equal(TrainingLoads.ShownDays, load.Days.Count);
            Assert.True(load.Days[^1].Fitness > 0);
            Assert.Null((await other.GetFromJsonAsync<TrainingLoadSummary>("/api/training-load", ct))!.ThresholdSpeedMS);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/training-load?timeZone=Nowhere", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/training-load", ct)).StatusCode);
            var predictions = (await owner.GetFromJsonAsync<RacePredictions>("/api/records/predictions", ct))!;
            Assert.Null(predictions.Basis); // passen ligger utanför fönstret
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/records/trend?distanceM=5000", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/records/predictions", ct)).StatusCode);


            var efforts = (await owner.GetFromJsonAsync<StoredBestEffort[]>($"/api/activities/{first.Id}/best-efforts", ct))!;
            Assert.Equal(new[] { 1000, 1609, 5000 }, efforts.Select(e => e.DistanceM));
            Assert.All(efforts, e => Assert.Equal(first.Id, e.ActivityId));
            Assert.Equal(1609.344 / 3, efforts[1].ElapsedS, precision: 8);
            Assert.Equal(start, efforts[0].AchievedAt);
            var records = await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct);
            Assert.All(records!, e => Assert.Equal(first.Id, e.ActivityId));
            var yearly = await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records?year=2026", ct);
            Assert.Equal(3, yearly!.Length);
            Assert.All(yearly, e => Assert.Equal(second.Id, e.ActivityId));
            Assert.Empty((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records?year=2024", ct))!);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/records?year=10000", ct)).StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/records", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync($"/api/activities/{first.Id}/best-efforts", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/activities/{first.Id}/best-efforts", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await other.PostAsync($"/api/activities/{first.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/activities/{first.Id}", ct)).StatusCode);
            var otherRecords = await other.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct);
            Assert.All(otherRecords!, e => Assert.Equal(otherRun.Id, e.ActivityId));

            // Recalculation replaces stale values, preserves fractional boundaries, and is repeatable.
            await conn.ExecuteAsync("""
                update activity_streams set t = array[0,100,150,300], dist = array[0,700,1200,1500]::real[]
                where activity_id = @id
                """, new { id = first.Id });
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.NoContent,
                    (await owner.PostAsync($"/api/activities/{first.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            var recalculated = Assert.Single((await owner.GetFromJsonAsync<StoredBestEffort[]>(
                $"/api/activities/{first.Id}/best-efforts", ct))!);
            Assert.Equal(100d * 200 / 700, recalculated.StartOffsetS, precision: 8);
            Assert.Equal(start.AddSeconds(recalculated.StartOffsetS).Ticks / 10, recalculated.AchievedAt.Ticks / 10);
            Assert.Empty((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records?year=2025", ct))!);
            // Samma omberäkning sparar löpintensiteten bakom träningsbelastningen: 700²/100 + 500²/50 + 300²/150.
            Assert.Equal(10500, await conn.ExecuteScalarAsync<double>(
                "select run_intensity from activities where id = @id", new { id = first.Id }), precision: 6);

            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest,
                (await owner.PostAsync($"/api/activities/{first.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            await TestSession.UseCsrfToken(owner, ct);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/activities/{first.Id}", ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select count(*) from best_efforts where activity_id = @id", new { id = first.Id }));
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/activities/{first.Id}/best-efforts", ct)).StatusCode);
            Assert.All((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!, e => Assert.Equal(second.Id, e.ActivityId));

            // Non-running activities and activities without streams cannot retain old records.
            await conn.ExecuteAsync("update activities set sport = 'cycling' where id = @id", new { id = second.Id });
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/activities/{second.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!);
            await conn.ExecuteAsync("""
                update activities set sport = 'running' where id = @id;
                update activity_streams set t = array[0,100,150,300], dist = array[0,null,1200,2200]::real[]
                where activity_id = @id
                """, new { id = second.Id });
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/activities/{second.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            var afterGap = Assert.Single((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!);
            Assert.Equal(150, afterGap.StartOffsetS);
            Assert.Equal(150, afterGap.ElapsedS);
            await conn.ExecuteAsync("update activity_streams set dist = null where activity_id = @id", new { id = second.Id });
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/activities/{second.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            Assert.Null(await conn.ExecuteScalarAsync<double?>(
                "select run_intensity from activities where id = @id", new { id = second.Id }));
            Assert.Empty((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!);
            await conn.ExecuteAsync("""
                delete from activity_streams where activity_id = @id
                """, new { id = second.Id });
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/activities/{second.Id}/best-efforts/recalculate", null, ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@ownerId, @otherId)", new { ownerId, otherId });
        }
    }

    private async Task<HttpClient> CreateRunner(NpgsqlConnection conn, Guid id, CancellationToken ct)
    {
        var email = $"best-effort-{id:N}@example.com";
        await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)", new { id, email });
        await conn.ExecuteAsync("insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await SupabaseCookieSession.SignInAsync(http, new AuthSession(id, email, "synthetic-access", "synthetic-refresh",
            DateTimeOffset.UtcNow.AddHours(1)), ct);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", http.Response.Headers.SetCookie.Select(c => c!.Split(';')[0])));
        await TestSession.UseCsrfToken(client, ct);
        return client;
    }

    private static async Task<ActivitySummary> Upload(HttpClient client, DateTime start, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(FitFile.Run(start, 2100));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "synthetic-run.fit");
        form.Add(new StringContent("long_run"), "tags");
        form.Add(new StringContent("easy"), "tags");
        using var response = await client.PostAsync("/api/activities", form, ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        var activity = (await response.Content.ReadFromJsonAsync<ActivitySummary>(ct))!;
        Assert.Equal(new[] { "long_run", "easy" }, activity.Tags);
        return activity;
    }
}
