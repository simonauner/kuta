using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class AdminTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    const string AdminHost = "admin.kutaapp.com";

    [Fact]
    public async Task Stats_are_served_only_to_listed_admins_on_the_admin_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var (adminEmail, otherEmail) = ($"admin-{Guid.NewGuid():N}@example.test", $"other-{Guid.NewGuid():N}@example.test");
        var admin = factory.CreateClient();
        var other = factory.CreateClient();
        try
        {
            await TestSession.CreateRunnerAsync(factory, admin, conn, adminEmail, "Synthetic admin", ct);
            await TestSession.CreateRunnerAsync(factory, other, conn, otherEmail, "Synthetic runner", ct);
            var adminId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @adminEmail", new { adminEmail });
            var app = factory.WithWebHostBuilder(b => b.UseSetting("Admin:Host", AdminHost).UseSetting("Admin:UserIds", $"{Guid.NewGuid()}, {adminId}"));

            async Task<HttpResponseMessage> Get(HttpClient session, string host, string path)
            {
                var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
                var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Host = host;
                foreach (var cookie in session.DefaultRequestHeaders.GetValues("Cookie")) request.Headers.Add("Cookie", cookie);
                return await client.SendAsync(request, ct);
            }

            var stats = await Get(admin, AdminHost, "/api/admin/stats");
            Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
            var body = await stats.Content.ReadFromJsonAsync<AdminStats>(ct);
            Assert.True(body!.Accounts >= 2 && body.SignedIn30Days >= 2);

            Assert.Equal(HttpStatusCode.NotFound, (await Get(other, AdminHost, "/api/admin/stats")).StatusCode);
            // The public host has no admin routes, and the admin host none of the app's other API.
            Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "kutaapp.com", "/api/admin/stats")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "kutaapp.com", "/app/admin")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, AdminHost, "/api/activities")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Get(admin, AdminHost, "/api/me")).StatusCode);
            Assert.Equal("/app/admin", (await Get(admin, AdminHost, "/")).Headers.Location?.ToString());
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@adminEmail, @otherEmail)", new { adminEmail, otherEmail });
        }
    }
}
