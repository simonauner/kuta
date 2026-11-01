using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Dynastream.Fit;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static TestSession;

// Requires local Supabase with Storage (see KutaFactory). The FIT files are created here with Garmin's SDK.
public class FitUploadTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Upload_stores_activity_streams_and_heart_rate_only_with_consent()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"fit-a-{Guid.NewGuid():N}@example.com";
        var other = $"fit-b-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await CreateRunnerAsync(factory, client, conn, runner, "Löparen", ct);
            var start = new System.DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc);
            var morning = FitFile.Run(start, seconds: 600);

            // The form preview saves nothing and shows no heart rate without consent
            var preview = await Upload(client, morning, ct, "/api/activities/preview");
            var previewed = await preview.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal((1800d, 600, "Garmin Fenix 5s", JsonValueKind.Null), (previewed.GetProperty("distanceM").GetDouble(),
                previewed.GetProperty("durationS").GetInt32(), previewed.GetProperty("device").GetString(),
                previewed.GetProperty("avgHr").ValueKind));
            Assert.Empty((await client.GetFromJsonAsync<ActivityPage>("/api/activities", ct))!.Items);

            // Without consent: the activity is saved, but neither heart rate nor raw file (which contains heart rate). The title from the form is kept.
            var upload = await Upload(client, morning, ct, title: " Morgonrunda ", description: " Lugnt pass\nMed vänner ");
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var created = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
            var id = created.GetProperty("id").GetGuid();
            Assert.Equal(JsonValueKind.Null, created.GetProperty("avgHr").ValueKind);
            var row = await conn.QuerySingleAsync<(string Sport, string? Title, int DurationS, double DistanceM, short? AvgHr, string? RawFileKey, bool HasRoute)>(
                "select sport, title, duration_s, distance_m, avg_hr, raw_file_key, route is not null from activities where id = @id", new { id });
            Assert.Equal(("running", (string?)"Morgonrunda", 600, 1800d, (short?)null, (string?)null, true), row);
            Assert.Equal("Lugnt pass\nMed vänner", (await client.GetFromJsonAsync<ActivityDetail>($"/api/activities/{id}", ct))!.Description);
            Assert.Equal(0, await StoredFiles(conn, runner));
            var streams = await conn.QuerySingleAsync<(int Points, int? Hr, int? Lat, int LastT)>(
                "select cardinality(t), cardinality(hr), cardinality(lat), t[cardinality(t)] from activity_streams where activity_id = @id", new { id });
            Assert.Equal((601, (int?)null, (int?)601, 600), streams);
            var details = await conn.QuerySingleAsync<(string? Device, short? Cadence, short? Temp)>(
                "select a.device, s.cadence[1], s.temp[1] from activities a join activity_streams s on s.activity_id = a.id where a.id = @id",
                new { id });
            Assert.Equal(("Garmin Fenix 5s", (short?)170, (short?)18), details);
            var laps = await conn.QuerySingleAsync<(int Count, int WithHr, short? Cadence, int SecondStart)>(
                """
                select count(*)::int, count(avg_hr)::int, max(avg_cadence),
                       max(start_s) filter (where lap_index = 1)
                from activity_laps where activity_id = @id
                """, new { id });
            Assert.Equal((10, 0, (short?)170, 60), laps); // lap heart rate follows the consent

            Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, morning, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, "inte en fit-fil"u8.ToArray(), ct)).StatusCode);
            using (var corrupt = await Upload(client, FitFile.CorruptWithValidChecksum(start), ct))
            {
                Assert.Equal(HttpStatusCode.BadRequest, corrupt.StatusCode);
                Assert.Equal("invalidFit", (await corrupt.Content.ReadFromJsonAsync<ApiError>(ct))!.Code);
            }

            // With consent: heart rate is saved, both summary and time series
            var consent = await client.PutAsync("/api/me/hr-consent", null, ct);
            Assert.True((await consent.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("hrConsent").GetBoolean());
            var evening = await Upload(client, FitFile.Run(start.AddHours(12), seconds: 300), ct);
            var eveningId = (await evening.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            var hr = await conn.QuerySingleAsync<(short? AvgHr, short? MaxHr, int? Points)>(
                "select a.avg_hr, a.max_hr, cardinality(s.hr) from activities a join activity_streams s on s.activity_id = a.id where a.id = @eveningId",
                new { eveningId });
            Assert.Equal(((short?)150, (short?)165, (int?)301), hr);
            Assert.Equal(5, await conn.ExecuteScalarAsync<int>(
                "select count(avg_hr) from activity_laps where activity_id = @eveningId", new { eveningId }));
            Assert.Equal(1, await StoredFiles(conn, runner));

            // Withdrawn consent deletes all stored heart rate, including the raw files
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/me/hr-consent", ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                """
                select count(*) from activities a join activity_streams s on s.activity_id = a.id
                where a.user_id = (select id from auth.users where email = @runner)
                  and (a.avg_hr is not null or s.hr is not null or a.raw_file_key is not null
                       or exists (select 1 from activity_laps l where l.activity_id = a.id and l.avg_hr is not null))
                """, new { runner }));
            Assert.Equal(0, await StoredFiles(conn, runner));

            var list = await client.GetFromJsonAsync<ActivityPage>("/api/activities", ct);
            Assert.Equal(2, list!.Items.Count);

            // Another user does not see the activities
            var otherClient = factory.CreateClient();
            await CreateRunnerAsync(factory, otherClient, conn, other, "Någon annan", ct);
            Assert.Empty((await otherClient.GetFromJsonAsync<ActivityPage>("/api/activities", ct))!.Items);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner, @other)", new { runner, other });
        }
    }

    [Fact]
    public async Task Delete_removes_own_activity_and_raw_file_but_not_someone_elses()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"del-a-{Guid.NewGuid():N}@example.com";
        var other = $"del-b-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        var otherClient = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await CreateRunnerAsync(factory, client, conn, runner, "Löparen", ct);
            await client.PutAsync("/api/me/hr-consent", null, ct);
            var fit = FitFile.Run(new System.DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc), seconds: 120);
            var id = (await (await Upload(client, fit, ct)).Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            Assert.Equal(1, await StoredFiles(conn, runner));

            // Activity page: the owner gets the activity with time series (heart rate included, since consent exists)
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/activities/{id}", ct);
            Assert.Equal(360d, detail.GetProperty("distanceM").GetDouble());
            var detailStreams = detail.GetProperty("streams");
            Assert.Equal(121, detailStreams.GetProperty("t").GetArrayLength());
            Assert.Equal(121, detailStreams.GetProperty("hr").GetArrayLength());
            Assert.Equal(59.33, detailStreams.GetProperty("lat")[0].GetDouble(), precision: 5);
            Assert.Equal(170, detailStreams.GetProperty("cadence")[0].GetInt32());
            Assert.Equal("Garmin Fenix 5s", detail.GetProperty("device").GetString());
            var detailLaps = detail.GetProperty("laps");
            Assert.Equal(2, detailLaps.GetArrayLength());
            Assert.Equal(155, detailLaps[0].GetProperty("avgHr").GetInt32());

            // Someone else can neither see nor delete the activity, and does not learn that it exists
            await CreateRunnerAsync(factory, otherClient, conn, other, "Någon annan", ct);
            Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from activities where id = @id", new { id }));

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select (select count(*) from activities where id = @id) + (select count(*) from activity_streams where activity_id = @id)",
                new { id }));
            Assert.Equal(0, await StoredFiles(conn, runner));
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/activities/{id}", ct)).StatusCode);

            // The same file can be uploaded again after deletion (e.g. to include heart rate)
            Assert.Equal(HttpStatusCode.Created, (await Upload(client, fit, ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner, @other)", new { runner, other });
        }
    }

    [Fact]
    public async Task Manual_activity_is_saved_without_heart_rate_unless_consented()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"manual-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await CreateRunnerAsync(factory, client, conn, runner, "Löparen", ct);
            var run = new ManualActivityRequest("running", "Löpband",
                DateTimeOffset.Parse("2026-10-01T18:00:00+02:00"), 1800, 5000, 0, 150);

            var created = await client.PostAsJsonAsync("/api/activities/manual", run with { Tags = ["workout"], Description = " Intervaller\n5 × 1000 m " }, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/activities/{id}", ct);
            Assert.Equal(("Löpband", "2026-10-01T16:00:00Z", 5000d, JsonValueKind.Null, JsonValueKind.Null),
                (detail.GetProperty("title").GetString(), detail.GetProperty("startedAt").GetString(),
                 detail.GetProperty("distanceM").GetDouble(), detail.GetProperty("avgHr").ValueKind,
                 detail.GetProperty("streams").ValueKind));
            Assert.Equal("Intervaller\n5 × 1000 m", detail.GetProperty("description").GetString());
            Assert.Equal("workout", Assert.Single(detail.GetProperty("tags").EnumerateArray()).GetString());
            Assert.Equal("manual", await conn.ExecuteScalarAsync<string>("select source from activities where id = @id", new { id }));

            await client.PutAsync("/api/me/hr-consent", null, ct);
            var withHr = await client.PostAsJsonAsync("/api/activities/manual", run, ct);
            Assert.Equal(150, (await withHr.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("avgHr").GetInt32());

            var invalid = await client.PostAsJsonAsync("/api/activities/manual", run with { DurationS = 0 }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalidActivity", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email = @runner", new { runner });
        }
    }

    // File parsing costs memory: upload and preview share a limit per user, not per IP.
    [Fact]
    public async Task Uploads_are_rate_limited_per_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"limit-a-{Guid.NewGuid():N}@example.com";
        var other = $"limit-b-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        var otherClient = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await CreateRunnerAsync(factory, client, conn, runner, "Löparen", ct);
            await CreateRunnerAsync(factory, otherClient, conn, other, "Grannen", ct);
            for (var i = 0; i < 15; i++)
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, [], ct, "/api/activities/preview")).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, [], ct)).StatusCode);
            }
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Upload(client, [], ct, "/api/activities/preview")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(otherClient, [], ct, "/api/activities/preview")).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner, @other)", new { runner, other });
        }
    }

    // Raw files in the local Supabase Storage (fit/<user-id>/…)
    static Task<int> StoredFiles(NpgsqlConnection conn, string email) => conn.ExecuteScalarAsync<int>(
        """
        select count(*) from storage.objects
        where bucket_id = 'fit-files' and name like 'fit/' || (select id from auth.users where email = @email) || '/%'
        """, new { email });

    static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] fit, CancellationToken ct,
        string path = "/api/activities", string? title = null, string? description = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(fit);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "pass.fit");
        if (title is not null) form.Add(new StringContent(title), "title");
        if (description is not null) form.Add(new StringContent(description), "description");
        return await client.PostAsync(path, form, ct);
    }
}

// Synthetic run: one point per second, 3 m/s eastward from Stockholm, heart rate 150–165.
static class FitFile
{
    const double SemicirclesPerDegree = 2147483648.0 / 180.0;

    // A run with a few bytes changed and the checksum recomputed, so the SDK fails with something other than FitException.
    public static byte[] CorruptWithValidChecksum(System.DateTime start)
    {
        var valid = Run(start, 60);
        var random = new Random(1);
        while (true)
        {
            var bytes = (byte[])valid.Clone();
            for (var i = 0; i < 3; i++) bytes[14 + random.Next(bytes.Length - 16)] = (byte)random.Next(256);
            var crc = CRC.Calc16(bytes, bytes.Length - 2);
            bytes[^2] = (byte)crc;
            bytes[^1] = (byte)(crc >> 8);
            try { FitImport.Parse(bytes); }
            catch (Exception e) when (e is not FitException) { return bytes; }
            catch (FitException) { }
        }
    }

    public static byte[] Run(System.DateTime start, int seconds, double latitude = 59.33)
    {
        using var stream = new MemoryStream();
        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(Manufacturer.Garmin);
        fileId.SetGarminProduct(GarminProduct.Fenix5s);
        fileId.SetSerialNumber(1);
        fileId.SetTimeCreated(new Dynastream.Fit.DateTime(start));
        encoder.Write(fileId);

        for (var i = 0; i <= seconds; i++)
        {
            var record = new RecordMesg();
            record.SetTimestamp(new Dynastream.Fit.DateTime(start.AddSeconds(i)));
            record.SetDistance(i * 3f);
            record.SetEnhancedSpeed(3f);
            record.SetEnhancedAltitude(20f);
            record.SetHeartRate((byte)(150 + i % 16));
            record.SetPositionLat((int)(latitude * SemicirclesPerDegree));
            record.SetPositionLong((int)((18.06 + i * 0.00005) * SemicirclesPerDegree));
            record.SetCadence(85); // per leg: 170 steps/min
            record.SetTemperature(18);
            encoder.Write(record);

            // One lap per minute (like the watch's auto lap), written when the lap ends
            if (i > 0 && i % 60 == 0)
            {
                var lap = new LapMesg();
                lap.SetTimestamp(new Dynastream.Fit.DateTime(start.AddSeconds(i)));
                lap.SetStartTime(new Dynastream.Fit.DateTime(start.AddSeconds(i - 60)));
                lap.SetTotalElapsedTime(60);
                lap.SetTotalTimerTime(60);
                lap.SetTotalDistance(180);
                lap.SetAvgHeartRate(155);
                lap.SetMaxHeartRate(165);
                lap.SetTotalAscent(2);
                lap.SetTotalDescent(1);
                lap.SetAvgCadence(85);
                encoder.Write(lap);
            }
        }

        var session = new SessionMesg();
        session.SetTimestamp(new Dynastream.Fit.DateTime(start.AddSeconds(seconds)));
        session.SetStartTime(new Dynastream.Fit.DateTime(start));
        session.SetSport(Sport.Running);
        session.SetTotalElapsedTime(seconds);
        session.SetTotalTimerTime(seconds);
        session.SetTotalDistance(seconds * 3f);
        session.SetEnhancedAvgSpeed(3f);
        session.SetAvgHeartRate(150);
        session.SetMaxHeartRate(165);
        session.SetTotalAscent(0);
        encoder.Write(session);

        encoder.Close();
        return stream.ToArray();
    }
}
