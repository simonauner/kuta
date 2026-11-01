using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ShareCodeGenerationTests
{
    [Fact]
    public void Codes_have_eight_characters_without_look_alikes()
    {
        Assert.DoesNotContain(ShareCodes.Alphabet, c => "0o1li".Contains(c));
        Assert.Equal(ShareCodes.Alphabet.Length, ShareCodes.Alphabet.Distinct().Count());
        var codes = Enumerable.Range(0, 1000).Select(_ => ShareCodes.Generate()).ToList();
        Assert.All(codes, code => Assert.Matches("^[a-hjkmnp-z2-9]{8}$", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
        // Every character of the alphabet is used, so the generator draws from all of it.
        Assert.Equal(ShareCodes.Alphabet.Order(), codes.SelectMany(c => c).Distinct().Order());
    }
}

public class ShareCodeTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Code_resolves_to_the_owner_until_replaced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var viewer = factory.CreateClient();
        var ownerEmail = $"share-owner-{Guid.NewGuid():N}@example.com";
        var viewerEmail = $"share-viewer-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, ownerEmail, "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, viewer, conn, viewerEmail, "Viewer", ct);
            var ownerId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @ownerEmail", new { ownerEmail });
            async Task<string> Code(string path) =>
                (await (await owner.PostAsync(path, null, ct)).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ShareCode>(ct))!.Code;

            var code = await Code("/api/me/share-code");
            Assert.Equal(code, await Code("/api/me/share-code"));
            Assert.Equal(ownerId, (await viewer.GetFromJsonAsync<SharedRunner>($"/api/share-codes/{code}", ct))!.RunnerId);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/share-codes/{ShareCodes.Generate()}", ct)).StatusCode);
            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/share-codes/{code}", ct)).StatusCode);

            var replaced = await Code("/api/me/share-code/replace");
            Assert.NotEqual(code, replaced);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/share-codes/{code}", ct)).StatusCode);
            Assert.Equal(ownerId, (await viewer.GetFromJsonAsync<SharedRunner>($"/api/share-codes/{replaced}", ct))!.RunnerId);
            Assert.Equal(replaced, await Code("/api/me/share-code"));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@ownerEmail, @viewerEmail)", new { ownerEmail, viewerEmail });
        }
    }

    [Fact]
    public async Task Lookups_are_rate_limited_per_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var guesser = factory.CreateClient();
        var email = $"share-limit-{Guid.NewGuid():N}@example.com";
        try
        {
            await TestSession.CreateRunnerAsync(factory, guesser, conn, email, "Guesser", ct);
            for (var i = 0; i < 30; i++)
                Assert.Equal(HttpStatusCode.NotFound, (await guesser.GetAsync($"/api/share-codes/{ShareCodes.Generate()}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await guesser.GetAsync($"/api/share-codes/{ShareCodes.Generate()}", ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }
    }
}
