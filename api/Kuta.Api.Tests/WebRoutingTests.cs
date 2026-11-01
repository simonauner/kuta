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
        await File.WriteAllTextAsync(Path.Combine(webRoot, "features.html"), "<h1>Available features</h1>", ct);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "privacy.html"), "<h1>Privacy policy</h1>", ct);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "terms.html"), "<h1>Terms of use</h1>", ct);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "app.html"), "<meta name=\"robots\" content=\"noindex\"><div>App shell</div>", ct);
        try
        {
            await using var factory = new KutaFactory()
                .WithWebHostBuilder(b => b.UseWebRoot(webRoot));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Contains("Public landing", await client.GetStringAsync("/", ct));
            foreach (var path in new[] { "/privacy", "/privacy/" })
            {
                var html = await client.GetStringAsync(path, ct);
                Assert.Contains("Privacy policy", html);
                Assert.DoesNotContain("App shell", html);
            }
            foreach (var path in new[] { "/terms", "/terms/" })
            {
                var html = await client.GetStringAsync(path, ct);
                Assert.Contains("Terms of use", html);
                Assert.DoesNotContain("App shell", html);
            }
            foreach (var path in new[] { "/features", "/features/" })
            {
                var html = await client.GetStringAsync(path, ct);
                Assert.Contains("Available features", html);
                Assert.DoesNotContain("App shell", html);
            }
            foreach (var path in new[] { "/app", "/app/", "/app/activities", "/app/activities/new", $"/app/activities/{Guid.NewGuid()}", $"/app/activities/{Guid.NewGuid()}/edit", "/app/stats", "/app/stats/analysis", "/app/profile", "/app/settings/equipment", "/app/settings/followers", $"/app/runners/{Guid.NewGuid()}", "/app/r/k7m2qx9p", "/app/settings", "/app/settings/privacy-places", "/app?confirm=failed", "/app?confirm=ok", "/app?reset=password", "/app?reset=failed" })
            {
                var html = await client.GetStringAsync(path, ct);
                Assert.Contains("App shell", html);
                Assert.Contains("noindex", html);
                Assert.DoesNotContain("Public landing", html);
            }
            foreach (var path in new[] { "/finns-inte", "/app/finns-inte", "/api/finns-inte", "/pass", "/statistik", "/profil", "/app/pass", "/app/statistik", "/app/profil", "/app/activities/finns-inte", "/app/runners/finns-inte", "/app/r/k7m2qx9", "/app/r/k7m2qx9pp", "/app/profile/equipment", "/app/settings/finns-inte" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, ct)).StatusCode);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
