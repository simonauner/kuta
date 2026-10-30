using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class LocaleTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("89.160.20.112", true)]
    [InlineData("::ffff:89.160.20.112", true)]
    [InlineData("2a00:801::1", true)]
    public void Matches_Swedish_IPv4_IPv6_and_mapped_addresses(string address, bool expected) =>
        Assert.Equal(expected, SwedishIp.Contains(IPAddress.Parse(address)));

    [Fact]
    public void Missing_address_is_not_Swedish() => Assert.False(SwedishIp.Contains(null));

    [Theory]
    [InlineData("89.160.20.112", true)]
    [InlineData("8.8.8.8", false)]
    public async Task Public_locale_endpoint_uses_connection_IP_and_is_not_cacheable(string ip, bool expected)
    {
        await using var localeFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(
                new ClientIpFilter(ip))));
        using var client = localeFactory.CreateClient();
        client.DefaultRequestHeaders.Add("CF-IPCountry", expected ? "US" : "SE");
        var response = await client.GetAsync("/api/locale", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<LocaleResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(expected, body!.IsSwedishIp);
    }

    private record LocaleResponse(bool IsSwedishIp);

    private sealed class ClientIpFilter(string ip) : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
