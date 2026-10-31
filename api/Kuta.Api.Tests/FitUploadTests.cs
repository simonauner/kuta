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

// Kräver lokal Supabase med Storage (se KutaFactory). FIT-filerna skapas här med Garmins SDK.
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
            await SignUpAsync(client, runner, "Löparen", ct);
            var start = new System.DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc);
            var morning = FitFile.Run(start, seconds: 600);

            // Förhandsvisningen för formuläret sparar ingenting och visar ingen puls utan samtycke
            var preview = await Upload(client, morning, ct, "/api/activities/preview");
            var previewed = await preview.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal((1800d, 600, "Garmin Fenix 5s", JsonValueKind.Null), (previewed.GetProperty("distanceM").GetDouble(),
                previewed.GetProperty("durationS").GetInt32(), previewed.GetProperty("device").GetString(),
                previewed.GetProperty("avgHr").ValueKind));
            Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/activities", ct))!);

            // Utan samtycke: passet sparas, men varken puls eller råfil (som innehåller puls). Titeln från formuläret följer med.
            var upload = await Upload(client, morning, ct, title: " Morgonrunda ");
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var created = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
            var id = created.GetProperty("id").GetGuid();
            Assert.Equal(JsonValueKind.Null, created.GetProperty("avgHr").ValueKind);
            var row = await conn.QuerySingleAsync<(string Sport, string? Title, int DurationS, double DistanceM, short? AvgHr, string? RawFileKey, bool HasRoute)>(
                "select sport, title, duration_s, distance_m, avg_hr, raw_file_key, route is not null from activities where id = @id", new { id });
            Assert.Equal(("running", (string?)"Morgonrunda", 600, 1800d, (short?)null, (string?)null, true), row);
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
            Assert.Equal((10, 0, (short?)170, 60), laps); // varvens puls följer samtycket

            Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, morning, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, "inte en fit-fil"u8.ToArray(), ct)).StatusCode);

            // Med samtycke: pulsen sparas, både sammanfattning och tidsserie
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

            // Återkallat samtycke raderar all sparad puls, även råfilerna
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/me/hr-consent", ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                """
                select count(*) from activities a join activity_streams s on s.activity_id = a.id
                where a.user_id = (select id from auth.users where email = @runner)
                  and (a.avg_hr is not null or s.hr is not null or a.raw_file_key is not null
                       or exists (select 1 from activity_laps l where l.activity_id = a.id and l.avg_hr is not null))
                """, new { runner }));
            Assert.Equal(0, await StoredFiles(conn, runner));

            var list = await client.GetFromJsonAsync<JsonElement[]>("/api/activities", ct);
            Assert.Equal(2, list!.Length);

            // En annan användare ser inte passen
            var otherClient = factory.CreateClient();
            await SignUpAsync(otherClient, other, "Någon annan", ct);
            Assert.Empty((await otherClient.GetFromJsonAsync<JsonElement[]>("/api/activities", ct))!);
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
            await SignUpAsync(client, runner, "Löparen", ct);
            await client.PutAsync("/api/me/hr-consent", null, ct);
            var fit = FitFile.Run(new System.DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc), seconds: 120);
            var id = (await (await Upload(client, fit, ct)).Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            Assert.Equal(1, await StoredFiles(conn, runner));

            // Passsidan: ägaren får passet med tidsserier (puls med, eftersom samtycke finns)
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

            // Någon annan kan varken se eller ta bort passet, och får inte veta att det finns
            await SignUpAsync(otherClient, other, "Någon annan", ct);
            Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from activities where id = @id", new { id }));

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select (select count(*) from activities where id = @id) + (select count(*) from activity_streams where activity_id = @id)",
                new { id }));
            Assert.Equal(0, await StoredFiles(conn, runner));
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/activities/{id}", ct)).StatusCode);

            // Samma fil går att ladda upp igen efter borttagning (t.ex. för att få med puls)
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
            await SignUpAsync(client, runner, "Löparen", ct);
            var run = new ManualActivityRequest("running", "Löpband",
                DateTimeOffset.Parse("2026-10-01T18:00:00+02:00"), 1800, 5000, 0, 150);

            var created = await client.PostAsJsonAsync("/api/activities/manual", run with { Tags = ["workout"] }, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/activities/{id}", ct);
            Assert.Equal(("Löpband", "2026-10-01T16:00:00Z", 5000d, JsonValueKind.Null, JsonValueKind.Null),
                (detail.GetProperty("title").GetString(), detail.GetProperty("startedAt").GetString(),
                 detail.GetProperty("distanceM").GetDouble(), detail.GetProperty("avgHr").ValueKind,
                 detail.GetProperty("streams").ValueKind));
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

    // Råfiler i den lokala Supabase Storage (fit/<användar-id>/…)
    static Task<int> StoredFiles(NpgsqlConnection conn, string email) => conn.ExecuteScalarAsync<int>(
        """
        select count(*) from storage.objects
        where bucket_id = 'fit-files' and name like 'fit/' || (select id from auth.users where email = @email) || '/%'
        """, new { email });

    static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] fit, CancellationToken ct,
        string path = "/api/activities", string? title = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(fit);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "pass.fit");
        if (title is not null) form.Add(new StringContent(title), "title");
        return await client.PostAsync(path, form, ct);
    }
}

// Syntetiskt löppass: en punkt per sekund, 3 m/s österut från Stockholm, puls 150–165.
static class FitFile
{
    const double SemicirclesPerDegree = 2147483648.0 / 180.0;

    public static byte[] Run(System.DateTime start, int seconds)
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
            record.SetPositionLat((int)(59.33 * SemicirclesPerDegree));
            record.SetPositionLong((int)((18.06 + i * 0.00005) * SemicirclesPerDegree));
            record.SetCadence(85); // per ben: 170 steg/min
            record.SetTemperature(18);
            encoder.Write(record);

            // Ett varv per minut (som klockans autolap), skrivet när varvet är slut
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
