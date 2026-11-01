using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

public class AuthRateLimitTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa::1", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    public void IPv6_clients_share_a_partition_per_64_network(string address, string partition) =>
        Assert.Equal(partition, AuthEndpoints.ClientPartition(IPAddress.Parse(address)));

    [Fact]
    public void Missing_address_has_a_shared_partition() => Assert.Equal("unknown", AuthEndpoints.ClientPartition(null));

    [Fact]
    public void Each_email_gets_ten_attempts_regardless_of_case()
    {
        var email = $"limit-{Guid.NewGuid():N}@example.com";
        for (var i = 0; i < 10; i++) Assert.Null(AuthEndpoints.LimitPerEmail(i % 2 == 0 ? email : email.ToUpperInvariant()));
        var limited = Assert.IsAssignableFrom<IStatusCodeHttpResult>(AuthEndpoints.LimitPerEmail($" {email} "));
        Assert.Equal(429, limited.StatusCode);
        Assert.Null(AuthEndpoints.LimitPerEmail($"other-{email}"));
    }
}
