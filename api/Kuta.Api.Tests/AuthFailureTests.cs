using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class AuthFailureTests
{
    [Theory]
    [InlineData("login")]
    [InlineData("signup")]
    [InlineData("recover")]
    public async Task Provider_timeout_returns_a_service_unavailable_error(string endpoint)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddHttpClient<SupabaseAuthClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new TimeoutHandler())));
        using var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<AntiforgeryToken>("/api/antiforgery", ct);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        var response = await client.PostAsJsonAsync($"/api/auth/{endpoint}",
            new { email = "timeout@example.com", password = "synthetic-password", acceptTerms = true }, ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>(ct))!;
        Assert.Equal("Inloggningen är tillfälligt otillgänglig. Försök igen om en stund.", error.Error);
        Assert.Equal("authUnavailable", error.Code);
    }

    // Supabase answers unknown addresses without checking a password; the API must not answer faster for them.
    [Fact]
    public async Task Failed_login_takes_a_fixed_minimum_time()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddHttpClient<SupabaseAuthClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new InvalidCredentialsHandler())));
        using var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<AntiforgeryToken>("/api/antiforgery", ct);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "unknown@example.com", password = "synthetic-password" }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(590));
    }

    sealed class InvalidCredentialsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error_code = "invalid_credentials" }),
            });
    }

    // With both keys set, sign-in, sign-up and password links need a token that Cloudflare accepts before Supabase is called.
    [Fact]
    public async Task Login_signup_and_recover_require_a_valid_Turnstile_token_when_configured()
    {
        var ct = TestContext.Current.CancellationToken;
        var cloudflare = new CloudflareHandler();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Turnstile:SiteKey", "synthetic-site-key");
            builder.UseSetting("Turnstile:SecretKey", "synthetic-secret");
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<TurnstileVerifier>().ConfigurePrimaryHttpMessageHandler(() => cloudflare);
                // Reaching Supabase shows the check passed; the stub answers like an unavailable provider.
                services.AddHttpClient<SupabaseAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new TimeoutHandler());
            });
        });
        using var client = factory.CreateClient();
        Assert.Equal("synthetic-site-key", (await client.GetFromJsonAsync<JsonElement>("/api/turnstile", ct)).GetProperty("siteKey").GetString());
        var csrf = await client.GetFromJsonAsync<AntiforgeryToken>("/api/antiforgery", ct);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        async Task<string?> Code(string endpoint, string? token)
        {
            var response = await client.PostAsJsonAsync($"/api/auth/{endpoint}", new
            {
                email = "turnstile@example.com", password = "synthetic-password", acceptTerms = true, turnstileToken = token,
            }, ct);
            return (await response.Content.ReadFromJsonAsync<ApiError>(ct))!.Code;
        }

        Assert.Equal("turnstileFailed", await Code("signup", null));
        Assert.Equal("turnstileFailed", await Code("signup", "rejected"));
        Assert.Equal("authUnavailable", await Code("signup", "accepted"));
        Assert.Equal("turnstileFailed", await Code("login", null));
        Assert.Equal("authUnavailable", await Code("login", "accepted"));
        Assert.Equal("turnstileFailed", await Code("recover", null));
        Assert.Equal("authUnavailable", await Code("recover", "accepted"));
        Assert.Equal("turnstileUnavailable", await Code("signup", "unreachable"));
        Assert.Equal("synthetic-secret", cloudflare.Secret);
    }

    [Fact]
    public async Task Turnstile_is_off_without_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>("/api/turnstile", ct)).GetProperty("siteKey").ValueKind);
    }

    // Answers Cloudflare's siteverify: "accepted" passes, "unreachable" fails like an outage, anything else is rejected.
    sealed class CloudflareHandler : HttpMessageHandler
    {
        public string? Secret { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));
            Secret = form["secret"];
            return form["response"] == "unreachable"
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = form["response"] == "accepted" }) };
        }
    }

    sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new TaskCanceledException("Synthetic provider timeout.", new TimeoutException());
    }
}
