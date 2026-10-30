using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class CanonicalHostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("kuta.fly.dev", "/profil?x=1", "https://kutaapp.com/profil?x=1")]
    [InlineData("www.kutaapp.com", "/", "https://kutaapp.com/")]
    [InlineData("kutaapp.com", "/api/health", null)]
    [InlineData("172.19.0.2", "/api/health", null)] // Flys hälsokontroll mot maskinens IP
    public async Task Other_public_hosts_redirect_to_the_canonical_host(string host, string path, string? expected)
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("CanonicalHost", "kutaapp.com"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        if (expected is null) Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal(expected, response.Headers.Location?.ToString());
        }
    }
}
