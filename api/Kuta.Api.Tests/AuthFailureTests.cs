using System.Net;
using System.Net.Http.Json;
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

    sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new TaskCanceledException("Synthetic provider timeout.", new TimeoutException());
    }
}
