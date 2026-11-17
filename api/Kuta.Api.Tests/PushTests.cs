using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class WebPushCryptoTests
{
    // RFC 8291, section 5.
    [Fact]
    public void Encrypts_the_rfc_example()
    {
        var b = WebPushCrypto.FromBase64Url;
        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = b("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = WebPushCrypto.PublicKey(b("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8")).Q,
        });
        var body = WebPushCrypto.Encrypt("When I grow up, I want to be a watermelon"u8.ToArray(),
            b("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            b("BTBZMqHH6r4Tts7J_aSIgg"), server, b("DGv6ra1nlYgDCS1FRnbzlw"));
        Assert.Equal("DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebPushCrypto.Base64Url(body));
    }

    [Fact]
    public void Vapid_token_is_signed_with_the_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parts = WebPushCrypto.VapidToken("https://fcm.googleapis.com", "https://kutaapp.com",
            DateTimeOffset.UtcNow.AddHours(12), key).Split('.');
        Assert.Contains("\"aud\":\"https://fcm.googleapis.com\"", Encoding.UTF8.GetString(WebPushCrypto.FromBase64Url(parts[1])));
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), WebPushCrypto.FromBase64Url(parts[2]),
            HashAlgorithmName.SHA256));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://web.push.apple.com/abc", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc", false)]
    [InlineData("https://fcm.googleapis.com.example.com/abc", false)]
    [InlineData("https://127.0.0.1/abc", false)]
    public void Only_browser_push_services_are_allowed(string endpoint, bool allowed) =>
        Assert.Equal(allowed, Push.IsPushService(endpoint));
}

public class PushTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    sealed class FakePushService : HttpMessageHandler
    {
        public readonly List<(string Endpoint, string? Authorization)> Requests = [];
        public HttpStatusCode Status = HttpStatusCode.Created;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("aes128gcm", Assert.Single(request.Content!.Headers.ContentEncoding));
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(Status));
        }
    }

    static object Subscription(string endpoint)
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new
        {
            endpoint,
            p256dh = WebPushCrypto.Base64Url(WebPushCrypto.PublicKeyBytes(browser.ExportParameters(false))),
            auth = WebPushCrypto.Base64Url(RandomNumberGenerator.GetBytes(16)),
            locale = "en",
        };
    }

    [Fact]
    public async Task Follow_request_and_approval_reach_the_subscribed_devices()
    {
        var ct = TestContext.Current.CancellationToken;
        var pushService = new FakePushService();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddHttpClient<WebPushSender>().ConfigurePrimaryHttpMessageHandler(() => pushService)));
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var anna = app.CreateClient();
        using var bo = app.CreateClient();
        await TestSession.CreateRunnerAsync(factory, anna, conn, $"anna-{Guid.NewGuid():N}@example.com", "Anna", ct);
        await TestSession.CreateRunnerAsync(factory, bo, conn, $"bo-{Guid.NewGuid():N}@example.com", "Bo", ct);
        var annaId = (await anna.GetFromJsonAsync<MeResponse>("/api/me", ct))!.Id;
        var boId = (await bo.GetFromJsonAsync<MeResponse>("/api/me", ct))!.Id;
        var annaEndpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";
        var boEndpoint = $"https://updates.push.services.mozilla.com/wpush/v2/{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.NoContent, (await anna.PutAsJsonAsync("/api/me/push-subscriptions", Subscription(annaEndpoint), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bo.PutAsJsonAsync("/api/me/push-subscriptions", Subscription(boEndpoint), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bo.PutAsJsonAsync("/api/me/push-subscriptions", Subscription("https://127.0.0.1/steal"), ct)).StatusCode);

        await bo.PostAsync($"/api/following/{annaId}", null, ct);
        await JobWorker.RunAllAsync(app.Services, annaId, ct);
        var request = Assert.Single(pushService.Requests);
        Assert.Equal(annaEndpoint, request.Endpoint);
        Assert.StartsWith("vapid t=", request.Authorization);

        // A gone subscription is removed; the approval still reaches the requester.
        pushService.Status = HttpStatusCode.Gone;
        await anna.PutAsync($"/api/followers/{boId}", null, ct);
        await JobWorker.RunAllAsync(app.Services, boId, ct);
        Assert.Equal(boEndpoint, pushService.Requests[^1].Endpoint);
        Assert.False(await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from push_subscriptions where user_id = @boId)", new { boId }));
    }

    [Fact]
    public async Task A_signed_out_device_gets_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var pushService = new FakePushService();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddHttpClient<WebPushSender>().ConfigurePrimaryHttpMessageHandler(() => pushService)));
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var anna = app.CreateClient();
        using var bo = app.CreateClient();
        await TestSession.CreateRunnerAsync(factory, anna, conn, $"anna-{Guid.NewGuid():N}@example.com", "Anna", ct);
        await TestSession.CreateRunnerAsync(factory, bo, conn, $"bo-{Guid.NewGuid():N}@example.com", "Bo", ct);
        var annaId = (await anna.GetFromJsonAsync<MeResponse>("/api/me", ct))!.Id;
        await anna.PutAsJsonAsync("/api/me/push-subscriptions", Subscription($"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}"), ct);
        await conn.ExecuteAsync("update user_sessions set ended_at = now() where user_id = @annaId", new { annaId });

        await bo.PostAsync($"/api/following/{annaId}", null, ct);
        await JobWorker.RunAllAsync(app.Services, annaId, ct);
        Assert.Empty(pushService.Requests);
    }

    [Fact]
    public async Task Group_requests_reach_the_admins_unless_the_type_is_off_and_approval_reaches_the_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var pushService = new FakePushService();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddHttpClient<WebPushSender>().ConfigurePrimaryHttpMessageHandler(() => pushService)));
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var anna = app.CreateClient();
        using var bo = app.CreateClient();
        using var cia = app.CreateClient();
        await TestSession.CreateRunnerAsync(factory, anna, conn, $"anna-{Guid.NewGuid():N}@example.com", "Anna", ct);
        await TestSession.CreateRunnerAsync(factory, bo, conn, $"bo-{Guid.NewGuid():N}@example.com", "Bo", ct);
        await TestSession.CreateRunnerAsync(factory, cia, conn, $"cia-{Guid.NewGuid():N}@example.com", "Cia", ct);
        var annaId = (await anna.GetFromJsonAsync<MeResponse>("/api/me", ct))!.Id;
        var boId = (await bo.GetFromJsonAsync<MeResponse>("/api/me", ct))!.Id;
        var ciaId = (await cia.GetFromJsonAsync<MeResponse>("/api/me", ct))!.Id;
        var code = ShareCodes.Generate();
        var groupId = await conn.ExecuteScalarAsync<Guid>("""
            with g as (insert into groups (name, invite_code, time_zone) values ('Synthetic group', @code, 'UTC') returning id)
            insert into group_members (group_id, user_id, role, accepted_at)
            select id, @annaId, 'admin', now() from g union all select id, @boId, 'admin', now() from g
            returning group_id
            """, new { code, annaId, boId });
        foreach (var (client, service) in new[] { (anna, "fcm.googleapis.com/fcm/send"), (bo, "updates.push.services.mozilla.com/wpush/v2"), (cia, "web.push.apple.com") })
            await client.PutAsJsonAsync("/api/me/push-subscriptions", Subscription($"https://{service}/{Guid.NewGuid():N}"), ct);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bo.PutAsJsonAsync("/api/me/push-preferences", new { disabled = new[] { "everything" } }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await bo.PutAsJsonAsync("/api/me/push-preferences", new { disabled = new[] { "groupJoinRequest" } }, ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await cia.PostAsync($"/api/group-invites/{code}", null, ct)).StatusCode);
        await JobWorker.RunAllAsync(app.Services, annaId, ct);
        await JobWorker.RunAllAsync(app.Services, boId, ct);
        Assert.Contains("fcm.googleapis.com", Assert.Single(pushService.Requests).Endpoint);

        await anna.PostAsync($"/api/groups/{groupId}/members/{ciaId}/accept", null, ct);
        await JobWorker.RunAllAsync(app.Services, ciaId, ct);
        Assert.Equal(2, pushService.Requests.Count);
        Assert.Contains("web.push.apple.com", pushService.Requests[^1].Endpoint);
    }

    sealed record MeResponse(Guid Id);
}
