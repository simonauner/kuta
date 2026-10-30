using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Register_login_logout_flow()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/activities", ct)).StatusCode);

            var bad = await client.PostAsJsonAsync("/api/auth/register",
                new { email = "inte-en-adress", password = "kort", displayName = "" }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

            var register = await client.PostAsJsonAsync("/api/auth/register",
                new { email = email.ToUpperInvariant(), password = "hemligt-lösen", displayName = "Testlöpare" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, register.StatusCode);

            var me = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/me", ct);
            Assert.Equal(email, me!["email"].ToString());
            Assert.Equal("Testlöpare", me["displayName"].ToString());
            await using (var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct))
                await conn.ExecuteAsync(
                    "insert into activities (user_id, source, started_at, duration_s, distance_m) values (@id, 'manual', now(), 1800, 5000)",
                    new { id = Guid.Parse(me["id"].ToString()!) });
            var activities = await client.GetStringAsync("/api/activities", ct);
            Assert.Contains("\"startedAt\"", activities);
            Assert.Contains("\"distanceM\":5000", activities);

            var duplicate = await client.PostAsJsonAsync("/api/auth/register",
                new { email, password = "hemligt-lösen", displayName = "Igen" }, ct);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            await client.PostAsync("/api/auth/logout", null, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", ct)).StatusCode);

            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "fel-lösen" }, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "hemligt-lösen" }, ct);
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", ct)).StatusCode);
        }
        finally
        {
            await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
            await conn.ExecuteAsync("delete from users where email = @email", new { email });
        }
    }
}
