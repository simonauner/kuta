using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class SecurityHeadersTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/me")] // 401 responses too
    public async Task Responses_forbid_scripts_framing_and_sniffing(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // The web has its CSP in HTML; the header adds frame-ancestors.
    [Fact]
    public async Task Web_responses_forbid_framing()
    {
        var response = await factory.CreateClient().GetAsync("/app/profil", TestContext.Current.CancellationToken);
        Assert.Equal("frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
    }
}
