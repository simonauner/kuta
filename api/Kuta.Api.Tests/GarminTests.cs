using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class GarminTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    // Synthetic Garmin: FIT for id=ok, 410 for id=gone, 200 for other calls. Records the calls for assertions.
    sealed class FakeGarmin(byte[] fit) : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri} {request.Headers.Authorization}");
            var url = request.RequestUri!.ToString();
            return Task.FromResult(url.Contains("id=gone") ? new HttpResponseMessage(HttpStatusCode.Gone)
                : url.Contains("id=ok") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fit) }
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    static GarminClient Client(FakeGarmin fake) => new(new HttpClient(fake), new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Garmin:ClientId"] = "synthetic-client", ["Garmin:ClientSecret"] = "synthetic-secret",
        }).Build());

    IDataProtectionProvider Provider => factory.Services.GetRequiredService<IDataProtectionProvider>();
    RawFileStore Store => factory.Services.GetRequiredService<RawFileStore>();

    static async Task<Guid> RunnerAsync(NpgsqlConnection conn)
    {
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@id);
            insert into users (id, display_name, default_activity_privacy) values (@id, 'Garmin runner', 'followers')
            """, new { id });
        return id;
    }

    Task<bool> ConnectAsync(NpgsqlConnection conn, Guid userId, string garminUserId) =>
        Garmin.SaveConnectionAsync(conn, Provider, userId, garminUserId, new GarminTokens("synthetic-access",
            "synthetic-refresh", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(90)),
            [GarminClient.ActivityExport], TestContext.Current.CancellationToken);

    static Task<Guid> ActivityAsync(NpgsqlConnection conn, Guid userId, string source) =>
        conn.QuerySingleAsync<Guid>("""
            insert into activities (user_id, source, started_at, duration_s, distance_m)
            values (@userId, @source, now(), 600, 2000) returning id
            """, new { userId, source });

    [Fact]
    public async Task Push_queues_files_for_connected_users_and_import_saves_private_garmin_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = await RunnerAsync(conn);
        var garminUserId = Guid.NewGuid().ToString("N");
        try
        {
            Assert.True(await ConnectAsync(conn, userId, garminUserId));
            var other = await RunnerAsync(conn);
            Assert.False(await ConnectAsync(conn, other, garminUserId));
            await conn.ExecuteAsync("delete from auth.users where id = @other", new { other });

            GarminFileNotice File(string garmin, string id, string host = "apis.garmin.com") => new(garmin, $"{id}-{garminUserId}",
                "FIT", $"https://{host}/wellness-api/rest/activityFile?id={id}&token=synthetic", "Forerunner 965",
                "Morning Run", null);
            await Garmin.HandlePushAsync(conn, Store, new GarminPush([
                File(garminUserId, "ok"), File(garminUserId, "gone"), File("unknown-garmin-user", "unknown"),
                File(garminUserId, "elsewhere", "example.com"),
            ], null, null), ct);

            Assert.Equal(2, await conn.ExecuteScalarAsync<int>("select count(*)::int from garmin_files where user_id = @userId", new { userId }));
            var job = await JobQueue.ClaimAsync(conn, userId, ct);
            Assert.Equal(JobKinds.GarminImport, job!.Kind);

            var fake = new FakeGarmin(FitFile.Run(DateTime.UtcNow.AddHours(-2), 600));
            var result = await Garmin.RunImportAsync(conn, Store, Client(fake), Provider, job, ct);

            Assert.Equal(new GarminImportResult(1, 1), result);
            Assert.All(fake.Requests, r => Assert.EndsWith("Bearer synthetic-access", r));
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("select count(*)::int from garmin_files where user_id = @userId", new { userId }));
            var activity = await conn.QuerySingleAsync<(Guid Id, string Privacy, string Device, string Title)>(
                "select id, privacy, device, title from activities where user_id = @userId and source = 'garmin'", new { userId });
            Assert.Equal(("private", "Forerunner 965", "Morning Run"), (activity.Privacy, activity.Device, activity.Title));
            // Privacy policy: Garmin activities may not be shared until Garmin approves showing them to others.
            var error = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "update activities set privacy = 'followers' where id = @id", new { id = activity.Id }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }
    }

    [Fact]
    public async Task Deregistration_deletes_only_that_users_Garmin_data_and_disconnect_calls_Garmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var (a, b) = (await RunnerAsync(conn), await RunnerAsync(conn));
        var (garminA, garminB) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        try
        {
            await ConnectAsync(conn, a, garminA);
            await ConnectAsync(conn, b, garminB);
            await ActivityAsync(conn, a, "garmin");
            var aFit = await ActivityAsync(conn, a, "fit");
            var bGarmin = await ActivityAsync(conn, b, "garmin");

            await Garmin.HandlePushAsync(conn, Store, new GarminPush(null, [new(garminA)], null), ct);

            var remaining = (await conn.QueryAsync<Guid>("select id from activities where user_id in (@a, @b)", new { a, b })).ToHashSet();
            Assert.Equal(new HashSet<Guid> { aFit, bGarmin }, remaining);
            Assert.Equal([b], await conn.QueryAsync<Guid>("select user_id from garmin_connections where user_id in (@a, @b)", new { a, b }));

            // Disconnecting in Kuta deregisters at Garmin with the user's token.
            var fake = new FakeGarmin([]);
            await Garmin.DeregisterAsync(conn, Client(fake), Provider, b, ct);
            Assert.Equal(["DELETE https://apis.garmin.com/partner-gateway/rest/user/registration Bearer synthetic-access"], fake.Requests);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id in (@a, @b)", new { a, b }); }
    }

    [Fact]
    public async Task Push_needs_no_session_or_csrf_and_disconnect_deletes_own_Garmin_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var anonymous = factory.CreateClient();
        var push = await anonymous.PostAsJsonAsync("/api/garmin/push",
            new { deregistrations = new[] { new { userId = "unknown-garmin-user" } } }, ct);
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        using var client = factory.CreateClient();
        var email = $"garmin-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, client, conn, email, "Garmin runner", ct);
            var userId = await conn.QuerySingleAsync<Guid>("select id from auth.users where email = @email", new { email });
            await ConnectAsync(conn, userId, Guid.NewGuid().ToString("N"));
            await ActivityAsync(conn, userId, "garmin");
            Assert.True((await client.GetFromJsonAsync<GarminStatus>("/api/garmin", ct))!.Connected);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/garmin", ct)).StatusCode);

            Assert.False((await client.GetFromJsonAsync<GarminStatus>("/api/garmin", ct))!.Connected);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("select count(*)::int from activities where user_id = @userId", new { userId }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }

    [Fact]
    public void Before_launch_only_listed_accounts_may_connect()
    {
        var (listed, other) = (Guid.NewGuid(), Guid.NewGuid());
        GarminClient With(Dictionary<string, string?> settings) => new(new HttpClient(new FakeGarmin([])),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        var keys = new Dictionary<string, string?> { ["Garmin:ClientId"] = "id", ["Garmin:ClientSecret"] = "secret" };

        Assert.False(With(keys).AvailableTo(listed));
        var allowList = With(new(keys) { ["Garmin:AllowedUserIds"] = $"not-a-guid, {listed}" });
        Assert.True(allowList.AvailableTo(listed));
        Assert.False(allowList.AvailableTo(other));
        Assert.True(With(new(keys) { ["Garmin:OpenToAll"] = "true" }).AvailableTo(other));
        Assert.False(With(new() { ["Garmin:OpenToAll"] = "true" }).AvailableTo(other));
    }

    [Fact]
    public void Push_source_must_be_in_Garmins_networks()
    {
        Assert.True(GarminEndpoints.FromGarmin(IPAddress.Parse("204.77.163.10")));
        Assert.True(GarminEndpoints.FromGarmin(IPAddress.Parse("::ffff:185.132.79.1")));
        Assert.False(GarminEndpoints.FromGarmin(IPAddress.Parse("203.0.113.5")));
        Assert.False(GarminEndpoints.FromGarmin(null));
    }
}
