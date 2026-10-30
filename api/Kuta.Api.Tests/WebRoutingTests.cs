using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class WebRoutingTests
{
    [Fact]
    public async Task Public_and_app_documents_are_separate_and_unknown_paths_return_404()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"kuta-web-{Guid.NewGuid()}");
        Directory.CreateDirectory(webRoot);
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<h1>Public landing</h1>", ct);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "app.html"), "<meta name=\"robots\" content=\"noindex\"><div>App shell</div>", ct);
        try
        {
            await using var factory = new KutaFactory()
                .WithWebHostBuilder(b => b.UseWebRoot(webRoot));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Contains("Public landing", await client.GetStringAsync("/", ct));
            foreach (var path in new[] { "/app", "/app/", "/app/activities", $"/app/activities/{Guid.NewGuid()}", "/app/stats", "/app/profile", "/app?confirm=failed", "/app?reset=password", "/app?reset=failed" })
            {
                var html = await client.GetStringAsync(path, ct);
                Assert.Contains("App shell", html);
                Assert.Contains("noindex", html);
                Assert.DoesNotContain("Public landing", html);
            }
            foreach (var path in new[] { "/finns-inte", "/app/finns-inte", "/api/finns-inte", "/pass", "/statistik", "/profil", "/app/pass", "/app/statistik", "/app/profil", "/app/activities/finns-inte" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, ct)).StatusCode);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
