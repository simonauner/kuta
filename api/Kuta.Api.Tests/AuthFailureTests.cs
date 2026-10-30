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
            new { email = "timeout@example.com", password = "synthetic-password" }, ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Inloggningen är tillfälligt otillgänglig. Försök igen om en stund.",
            (await response.Content.ReadFromJsonAsync<ApiError>(ct))!.Error);
    }

    sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new TaskCanceledException("Synthetic provider timeout.", new TimeoutException());
    }
}
