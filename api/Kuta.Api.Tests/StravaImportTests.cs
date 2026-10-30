using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Xunit;
using static TestSession;

public class StravaParserTests
{
    [Fact]
    public void Csv_uses_SI_columns_and_preserves_quoted_multiline_titles()
    {
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(StravaFixture.Csv(
            "1,\"Oct 1, 2026, 6:30:00 AM\",\"Morning, \"\"run\"\"\nwith friends\",Run,,600,1.80,600.0,599.0,1800.0,150,165,3,10")));
        var row = Assert.Single(StravaImport.ReadManifest(csv));
        Assert.Equal("Morning, \"run\"\nwith friends", row.Title);
        Assert.Equal(1800, row.Summary.DistanceM);
        Assert.Equal(600, row.Summary.DurationS);
        Assert.Equal(599, row.Summary.MovingS);
        Assert.Equal(DateTimeKind.Utc, row.Summary.StartedAt.Kind);
    }

    [Fact]
    public void Gpx_reads_namespaced_heart_rate_and_distance_without_joining_segments()
    {
        var summary = StravaFixture.Summary();
        var parsed = StravaImport.ParseGpx(Encoding.UTF8.GetBytes(StravaFixture.Gpx), summary);
        Assert.Equal(new[] { 0, 60, 120 }, parsed.T);
        Assert.Equal((short)150, parsed.Hr![0]);
        Assert.Null(parsed.Hr[1]);
        Assert.InRange(parsed.Dist![1]!.Value, 50, 60);
        Assert.Equal(parsed.Dist[1], parsed.Dist[2]);
        Assert.InRange(parsed.Speed![1]!.Value, 0.9f, 1f);
        Assert.Null(parsed.Speed[2]);
        Assert.Equal(1800, parsed.DistanceM); // CSV-sammanfattningen behålls.
        Assert.NotNull(parsed.Route);
        Assert.Null(parsed.WithoutHeartRate().Hr);
    }

    [Theory]
    [InlineData("../private.fit")]
    [InlineData("/activities/file.fit")]
    [InlineData("activities\\file.fit")]
    public void Csv_rejects_unsafe_paths(string path)
    {
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(StravaFixture.Csv(StravaFixture.Row("1", path))));
        Assert.Throws<InvalidDataException>(() => StravaImport.ReadManifest(csv));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    public void Csv_rejects_invalid_distances(string distance)
    {
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(StravaFixture.Csv(StravaFixture.Row("1", "").Replace(",1800.0,", $",{distance},"))));
        Assert.Throws<InvalidDataException>(() => StravaImport.ReadManifest(csv));
    }

    [Fact]
    public void Gpx_rejects_DTD_and_unordered_times()
    {
        Assert.Throws<System.Xml.XmlException>(() => StravaImport.ParseGpx(
            "<!DOCTYPE gpx [<!ENTITY secret SYSTEM 'file:///etc/passwd'>]><gpx>&secret;</gpx>"u8.ToArray(), StravaFixture.Summary()));
        Assert.Throws<InvalidDataException>(() => StravaImport.ParseGpx(
            Encoding.UTF8.GetBytes(StravaFixture.Gpx.Replace("06:31:00", "06:29:00")), StravaFixture.Summary()));
    }

    [Fact]
    public void Gzip_is_bounded_after_decompression()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using var entry = zip.CreateEntry("large.fit.gz").Open();
            using var gzip = new GZipStream(entry, CompressionLevel.Fastest);
            gzip.Write(new byte[StravaImport.MaxFileBytes + 1]);
        }
        memory.Position = 0;
        using var archive = new ZipArchive(memory);
        Assert.Throws<InvalidDataException>(() => StravaImport.ReadFile(archive.Entries[0]));
    }
}

public class StravaUploadTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Archive_imports_FIT_GPX_and_manual_entries_with_consent_deduplication_and_private_access()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = $"strava-{Guid.NewGuid():N}@example.com";
        var other = $"strava-other-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await CreateRunner(client, conn, runner, ct);
            var archive = StravaFixture.Archive();
            var response = await Upload(client, archive, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<StravaImportResult>(ct))!;
            Assert.Equal(3, result.Imported);
            Assert.Equal(0, result.Duplicates);
            Assert.Equal(new[] { "4", "5" }, result.FailedActivityIds);
            var rows = (await conn.QueryAsync<(Guid Id, string? Title, string Privacy, short? Hr, string? Key)>(
                "select id, title, privacy, avg_hr, raw_file_key from activities where user_id = (select id from auth.users where email = @runner)", new { runner })).ToArray();
            Assert.Equal(3, rows.Length);
            var fitId = await conn.ExecuteScalarAsync<Guid>(
                "select id from activities where user_id=(select id from auth.users where email=@runner) and strava_activity_id='1'", new { runner });
            var details = await conn.QuerySingleAsync<(string Device, short Cadence, short Temp)>(
                "select a.device, s.cadence[1], s.temp[1] from activities a join activity_streams s on s.activity_id=a.id where a.id=@fitId", new { fitId });
            Assert.Equal(("Garmin Fenix 5s", (short)170, (short)18), details);
            var laps = await conn.QuerySingleAsync<(int Count, int WithHr)>(
                "select count(*)::int, count(avg_hr)::int from activity_laps where activity_id=@fitId", new { fitId });
            Assert.Equal((10, 0), laps);
            var efforts = (await client.GetFromJsonAsync<StoredBestEffort[]>($"/api/activities/{fitId}/best-efforts", ct))!;
            Assert.Equal(new[] { 1000, 1609 }, efforts.Select(e => e.DistanceM));
            Assert.Equal(1000d / 3, efforts[0].ElapsedS, precision: 8);
            Assert.Equal(1609.344 / 3, efforts[1].ElapsedS, precision: 8);
            Assert.All(rows, row => { Assert.Equal("private", row.Privacy); Assert.Null(row.Hr); Assert.Null(row.Key); });
            Assert.All(rows, row => Assert.Equal("Morning, run", row.Title));
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select count(*) from activity_streams s join activities a on a.id=s.activity_id where a.user_id=(select id from auth.users where email=@runner) and s.hr is not null", new { runner }));
            // Hashen gäller den uppackade filen: samma FIT via vanlig uppladdning ger också en dubblett.
            using (var form = new MultipartFormDataContent())
            {
                form.Add(new ByteArrayContent(FitFile.Run(new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc), 600)), "file", "run.fit");
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/activities", form, ct)).StatusCode);
            }
            var retry = (await (await Upload(client, archive, ct)).Content.ReadFromJsonAsync<StravaImportResult>(ct))!;
            Assert.Equal(0, retry.Imported);
            Assert.Equal(3, retry.Duplicates);

            var otherClient = factory.CreateClient();
            await CreateRunner(otherClient, conn, other, ct);
            Assert.Empty((await otherClient.GetFromJsonAsync<JsonElement[]>("/api/activities", ct))!);
            foreach (var row in rows)
            {
                Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/activities/{row.Id}", ct)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/activities/{row.Id}", ct)).StatusCode);
            }
            // Samma Strava-ID får importeras till ett annat konto. Puls sparas bara med dess samtycke.
            (await otherClient.PutAsync("/api/me/hr-consent", null, ct)).EnsureSuccessStatusCode();
            Assert.Equal(3, (await (await Upload(otherClient, archive, ct)).Content.ReadFromJsonAsync<StravaImportResult>(ct))!.Imported);
            Assert.Equal(2, await conn.ExecuteScalarAsync<int>(
                "select count(*) from activities where user_id=(select id from auth.users where email=@other) and raw_file_key is not null and avg_hr=150", new { other }));
            (await otherClient.DeleteAsync("/api/me/hr-consent", ct)).EnsureSuccessStatusCode();
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select count(*) from activities where user_id=(select id from auth.users where email=@other) and (raw_file_key is not null or avg_hr is not null or max_hr is not null)", new { other }));
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select count(*) from storage.objects where bucket_id='fit-files' and name like 'fit/' || (select id from auth.users where email=@other) || '/%'", new { other }));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@runner,@other)", new { runner, other });
        }
    }

    [Fact]
    public async Task Invalid_archive_and_missing_CSRF_do_not_import_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"strava-invalid-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await CreateRunner(client, conn, email, ct);
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, "not zip"u8.ToArray(), ct)).StatusCode);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, StravaFixture.Archive(), ct)).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select count(*) from activities where user_id=(select id from auth.users where email=@email)", new { email }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email=@email", new { email }); }
    }

    private async Task CreateRunner(HttpClient client, NpgsqlConnection conn, string email, CancellationToken ct)
    {
        var id = await conn.ExecuteScalarAsync<Guid>(
            "insert into auth.users (id,email) values (gen_random_uuid(),@email) returning id", new { email });
        await conn.ExecuteAsync("insert into users (id,display_name) values (@id,'Synthetic runner')", new { id });
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await SupabaseCookieSession.SignInAsync(context, new AuthSession(id, email, "synthetic-access", "synthetic-refresh",
            DateTimeOffset.UtcNow.AddHours(1)), ct);
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
            context.Response.Headers.SetCookie.Select(cookie => cookie!.Split(';')[0])));
        await UseCsrfToken(client, ct);
    }

    internal static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "strava.zip");
        return await client.PostAsync("/api/activities/import/strava", form, ct);
    }
}

static class StravaFixture
{
    public const string Gpx = """
        <gpx xmlns="http://www.topografix.com/GPX/1/1" xmlns:gpxtpx="http://www.garmin.com/xmlschemas/TrackPointExtension/v1">
          <trk><trkseg>
            <trkpt lat="59.33" lon="18.06"><ele>20</ele><time>2026-10-01T06:30:00Z</time><extensions><gpxtpx:TrackPointExtension><gpxtpx:hr>150</gpxtpx:hr></gpxtpx:TrackPointExtension></extensions></trkpt>
            <trkpt lat="59.3305" lon="18.06"><ele>21</ele><time>2026-10-01T06:31:00Z</time></trkpt>
          </trkseg><trkseg>
            <trkpt lat="59.34" lon="18.07"><time>2026-10-01T06:32:00Z</time></trkpt>
          </trkseg></trk>
        </gpx>
        """;
    public static string Csv(params string[] rows) =>
        "Activity ID,Activity Date,Activity Name,Activity Type,Filename,Elapsed Time,Distance,Elapsed Time,Moving Time,Distance,Average Heart Rate,Max Heart Rate,Average Speed,Elevation Gain\n" + string.Join('\n', rows);
    public static string Row(string id, string path) =>
        $"{id},\"Oct 1, 2026, 6:30:00 AM\",\"Morning, run\",Run,{path},600,1.80,600.0,599.0,1800.0,150,165,3,10";
    public static ParsedActivity Summary()
    {
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(Csv(Row("1", ""))));
        return StravaImport.ReadManifest(csv)[0].Summary;
    }
    public static byte[] Archive()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Write(string name, byte[] bytes, bool gzip = false)
            {
                using var entry = zip.CreateEntry(name).Open();
                if (gzip) { using var gz = new GZipStream(entry, CompressionLevel.Fastest); gz.Write(bytes); }
                else entry.Write(bytes);
            }
            Write("activities.csv", Encoding.UTF8.GetBytes(Csv(Row("1", "activities/1.fit.gz"), Row("2", "activities/2.gpx.gz"),
                Row("3", ""), Row("4", "activities/missing.gpx"), Row("5", "activities/broken.fit"))));
            Write("activities/1.fit.gz", FitFile.Run(new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc), 600), true);
            Write("activities/2.gpx.gz", Encoding.UTF8.GetBytes(Gpx), true);
            Write("activities/broken.fit", "broken"u8.ToArray());
        }
        return memory.ToArray();
    }
}
