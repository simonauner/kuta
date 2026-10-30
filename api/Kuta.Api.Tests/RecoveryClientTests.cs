using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

public class RecoveryClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Recovery_hides_provider_rejections_and_email_cooldown(HttpStatusCode status)
    {
        var client = CreateClient(status);
        await client.RecoverAsync("synthetic@example.com", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Recovery_reports_provider_outage()
    {
        var client = CreateClient(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.RecoverAsync("synthetic@example.com", TestContext.Current.CancellationToken));
    }

    static SupabaseAuthClient CreateClient(HttpStatusCode status) => new(
        new HttpClient(new ResponseHandler(status)),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supabase:Url"] = "http://supabase.test",
            ["Supabase:PublishableKey"] = "synthetic-key",
        }).Build(), new HttpContextAccessor());

    sealed class ResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
