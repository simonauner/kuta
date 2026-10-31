using Amazon.S3;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class DataExportTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Export_contains_only_owners_data_and_original_bytes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var store = factory.Services.GetRequiredService<RawFileStore>();
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var files = new List<string>();
        try
        {
            foreach (var userId in users)
            {
                await CreateProfileAsync(conn, userId);
                var place = await PrivacyPlaces.CreateAsync(conn, userId, new PrivacyPlaceRequest("Synthetic home", 59, 18), ct);
                var goalId = await Goals.CreateAsync(conn, userId, new GoalRequest("running", "distance", "week", 10_000, true),
                    "UTC", DateTimeOffset.UtcNow, ct);
                var shoeId = await conn.ExecuteScalarAsync<Guid>("""
                    insert into equipment (user_id, name, kind) values (@userId, 'Synthetic shoe', 'shoe') returning id
                    """, new { userId });
                var bytes = FitFile.Run(DateTime.UtcNow, 60);
                var parsed = FitImport.Parse(bytes)! with
                {
                    Laps = [new ParsedLap(0, 60, 60, 200, 145, 160, null, null, 170)],
                    Parts = [new ActivityPart(0, "running", 0, 60, 200, true)],
                };
                var id = (await ActivityStorage.SaveAsync(conn, store, userId, parsed, "fit", "Synthetic run", null,
                    bytes, ".fit", ct, ["race"], [shoeId], "Line one\nLine two"))!.Value;
                files.Add(await conn.QuerySingleAsync<string>("select raw_file_key from activities where id = @id", new { id }));
                var manual = await ActivityStorage.SaveManualAsync(conn, userId,
                    new ManualActivityRequest("running", "Manual run", DateTimeOffset.UtcNow, 600, 2000, null, null), ct);
                await conn.ExecuteAsync("""
                    insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
                    values (@id, @userId, 1000, 300, 0, now())
                    """, new { id, userId });

                await using var archive = await DataExport.CreateAsync(conn, store, userId, ct);
                using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
                Assert.Equal(8, zip.Entries.Count);
                var places = await ReadJsonAsync(zip, "privacy-places.json", ct);
                Assert.Equal(place.Id, Assert.Single(places.EnumerateArray()).GetProperty("id").GetGuid());
                var goals = await ReadJsonAsync(zip, "goals.json", ct);
                Assert.Equal(goalId, Assert.Single(goals.EnumerateArray()).GetProperty("id").GetGuid());
                var profile = await ReadJsonAsync(zip, "profile.json", ct);
                Assert.Equal(userId, profile.GetProperty("id").GetGuid());
                Assert.Equal("private", profile.GetProperty("defaultActivityPrivacy").GetString());
                Assert.False(profile.TryGetProperty("password", out _));
                var equipment = await ReadJsonAsync(zip, "equipment.json", ct);
                Assert.Equal(shoeId, Assert.Single(equipment.EnumerateArray()).GetProperty("id").GetGuid());
                var exported = await ReadJsonAsync(zip, $"activities/{id}.json", ct);
                var detail = exported.GetProperty("activity");
                Assert.Equal("Line one\nLine two", detail.GetProperty("description").GetString());
                Assert.Equal("race", detail.GetProperty("tags")[0].GetString());
                Assert.Equal(shoeId, detail.GetProperty("equipmentIds")[0].GetGuid());
                Assert.Equal(145, detail.GetProperty("laps")[0].GetProperty("avgHr").GetInt32());
                Assert.Single(detail.GetProperty("parts").EnumerateArray());
                Assert.Single(detail.GetProperty("originalParts").EnumerateArray());
                Assert.NotEmpty(detail.GetProperty("streams").GetProperty("t").EnumerateArray());
                Assert.Equal(parsed.Lat, detail.GetProperty("streams").GetProperty("lat").EnumerateArray()
                    .Select(point => point.ValueKind == JsonValueKind.Null ? (double?)null : point.GetDouble()).ToArray());
                Assert.Equal(parsed.Lon, detail.GetProperty("streams").GetProperty("lon").EnumerateArray()
                    .Select(point => point.ValueKind == JsonValueKind.Null ? (double?)null : point.GetDouble()).ToArray());
                Assert.Single(exported.GetProperty("bestEfforts").EnumerateArray());
                await using var raw = zip.GetEntry(exported.GetProperty("rawFile").GetString()!)!.Open();
                using var copy = new MemoryStream();
                await raw.CopyToAsync(copy, ct);
                Assert.Equal(bytes, copy.ToArray());
                var manualJson = await ReadJsonAsync(zip, $"activities/{manual!.Id}.json", ct);
                Assert.Equal(JsonValueKind.Null, manualJson.GetProperty("rawFile").ValueKind);
                Assert.Equal(JsonValueKind.Null, manualJson.GetProperty("activity").GetProperty("streams").ValueKind);
            }
        }
        finally
        {
            await store.DeleteAsync(files, ct);
            await conn.ExecuteAsync("delete from auth.users where id = any(@users)", new { users });
        }
    }

    [Fact]
    public async Task Export_includes_incoming_and_outgoing_follows_but_not_unrelated_relations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        try
        {
            foreach (var id in ids) await CreateProfileAsync(conn, id);
            await conn.ExecuteAsync("""
                insert into follows (follower_id, following_id, accepted_at)
                values (@owner, @following, null), (@follower, @owner, now()), (@following, @unrelated, now())
                """, new { owner = ids[0], following = ids[1], follower = ids[2], unrelated = ids[3] });
            await using var archive = await DataExport.CreateAsync(conn, factory.Services.GetRequiredService<RawFileStore>(), ids[0], ct);
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            var follows = (await ReadJsonAsync(zip, "follows.json", ct)).EnumerateArray().ToArray();
            Assert.Equal(2, follows.Length);
            Assert.All(follows, f => Assert.True(f.GetProperty("followerId").GetGuid() == ids[0] || f.GetProperty("followingId").GetGuid() == ids[0]));
            Assert.Contains(follows, f => f.GetProperty("acceptedAt").ValueKind == JsonValueKind.Null);
            Assert.Contains(follows, f => f.GetProperty("acceptedAt").ValueKind == JsonValueKind.String);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids }); }
    }

    [Fact]
    public async Task Export_waits_for_consent_withdrawal_and_reads_the_new_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var revoke = await db.OpenConnectionAsync(ct);
        await using var export = await db.OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        await CreateProfileAsync(revoke, userId);
        try
        {
            var id = await revoke.ExecuteScalarAsync<Guid>("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, avg_hr)
                values (@userId, 'manual', now(), 600, 2000, 150) returning id
                """, new { userId });
            Task<FileStream> exporting;
            await using (var tx = await revoke.BeginTransactionAsync(ct))
            {
                await revoke.ExecuteAsync("select id from users where id = @userId for update", new { userId }, tx);
                exporting = DataExport.CreateAsync(export, factory.Services.GetRequiredService<RawFileStore>(), userId, ct);
                await DatabaseWait.UntilBlockedAsync(revoke, export.ProcessID, ct);
                await revoke.ExecuteAsync("update users set hr_consent_at = null where id = @userId", new { userId }, tx);
                await revoke.ExecuteAsync("update activities set avg_hr = null where id = @id", new { id }, tx);
                await tx.CommitAsync(ct);
            }
            await using var archive = await exporting;
            using var zip = new ZipArchive(archive);
            var activity = await ReadJsonAsync(zip, $"activities/{id}.json", ct);
            Assert.Equal(JsonValueKind.Null, activity.GetProperty("activity").GetProperty("avgHr").ValueKind);
        }
        finally { await revoke.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }
    }

    [Fact]
    public async Task Missing_stored_file_fails_the_export_and_preserves_the_activity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        await CreateProfileAsync(conn, userId);
        try
        {
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, raw_file_key)
                values (@userId, 'fit', now(), 600, 2000, @key)
                """, new { userId, key = $"fit/{userId}/missing.fit" });
            await Assert.ThrowsAnyAsync<AmazonS3Exception>(() => DataExport.CreateAsync(conn,
                factory.Services.GetRequiredService<RawFileStore>(), userId, ct));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>(
                "select count(*) from activities where user_id = @userId", new { userId }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }
    }

    [Fact]
    public async Task Http_export_requires_a_session_and_returns_a_private_zip_even_with_no_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/export", ct)).StatusCode);
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var email = $"export-{Guid.NewGuid():N}@example.com";
        await TestSession.CreateRunnerAsync(factory, client, conn, email, "Export runner", ct);
        try
        {
            using var response = await client.GetAsync("/api/me/export", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Contains("kuta-export.zip", response.Content.Headers.ContentDisposition!.ToString());
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var zip = new ZipArchive(stream);
            Assert.Equal(5, zip.Entries.Count);
            Assert.Equal(email, (await ReadJsonAsync(zip, "profile.json", ct)).GetProperty("email").GetString());
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }

    private static Task CreateProfileAsync(NpgsqlConnection conn, Guid userId) => conn.ExecuteAsync("""
        insert into auth.users (id, email) values (@userId, @email);
        insert into users (id, display_name, hr_consent_at) values (@userId, 'Export runner', now())
        """, new { userId, email = $"export-{userId:N}@example.com" });

    private static async Task<JsonElement> ReadJsonAsync(ZipArchive zip, string name, CancellationToken ct)
    {
        await using var stream = zip.GetEntry(name)!.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return document.RootElement.Clone();
    }
}
