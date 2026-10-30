using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class SecurityHeadersTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/me")] // även 401-svar
    public async Task Responses_forbid_scripts_framing_and_sniffing(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
    }

    // Webben (i drift index.html via fallback) har sin CSP i meta-taggen; headern lägger till frame-ancestors
    [Fact]
    public async Task Web_responses_forbid_framing()
    {
        var response = await factory.CreateClient().GetAsync("/profil", TestContext.Current.CancellationToken);
        Assert.Equal("frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
    }
}
