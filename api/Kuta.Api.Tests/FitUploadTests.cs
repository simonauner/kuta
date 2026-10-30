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

            // Utan samtycke: passet sparas, men varken puls eller råfil (som innehåller puls)
            var upload = await Upload(client, morning, ct);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var created = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
            var id = created.GetProperty("id").GetGuid();
            Assert.Equal(JsonValueKind.Null, created.GetProperty("avgHr").ValueKind);
            var row = await conn.QuerySingleAsync<(string Sport, int DurationS, double DistanceM, short? AvgHr, string? RawFileKey, bool HasRoute)>(
                "select sport, duration_s, distance_m, avg_hr, raw_file_key, route is not null from activities where id = @id", new { id });
            Assert.Equal(("running", 600, 1800d, (short?)null, (string?)null, true), row);
            Assert.Equal(0, await StoredFiles(conn, runner));
            var streams = await conn.QuerySingleAsync<(int Points, int? Hr, int? Lat, int LastT)>(
                "select cardinality(t), cardinality(hr), cardinality(lat), t[cardinality(t)] from activity_streams where activity_id = @id", new { id });
            Assert.Equal((601, (int?)null, (int?)601, 600), streams);

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
            Assert.Equal(1, await StoredFiles(conn, runner));

            // Återkallat samtycke raderar all sparad puls, även råfilerna
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/me/hr-consent", ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                """
                select count(*) from activities a join activity_streams s on s.activity_id = a.id
                where a.user_id = (select id from auth.users where email = @runner)
                  and (a.avg_hr is not null or s.hr is not null or a.raw_file_key is not null)
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

    // Råfiler i den lokala Supabase Storage (fit/<användar-id>/…)
    static Task<int> StoredFiles(NpgsqlConnection conn, string email) => conn.ExecuteScalarAsync<int>(
        """
        select count(*) from storage.objects
        where bucket_id = 'fit-files' and name like 'fit/' || (select id from auth.users where email = @email) || '/%'
        """, new { email });

    static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] fit, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(fit);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "pass.fit");
        return await client.PostAsync("/api/activities", form, ct);
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
        fileId.SetManufacturer(Manufacturer.Development);
        fileId.SetProduct(1);
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
            encoder.Write(record);
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
