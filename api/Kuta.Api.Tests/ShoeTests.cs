using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ShoeInputTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("  ", "Brand", false)]
    [InlineData(" Daily ", null, true)]
    [InlineData("Daily", "  ", true)]
    public void Name_is_required_and_brand_optional(string? name, string? brand, bool valid) =>
        Assert.Equal(valid, new CreateShoeRequest(name, brand).Validate() is null);

    [Fact]
    public void Fields_are_limited_to_100_trimmed_characters()
    {
        Assert.Null(new CreateShoeRequest(new string('a', 100), new string('b', 100)).Validate());
        Assert.NotNull(new CreateShoeRequest(new string('a', 101), null).Validate());
        Assert.NotNull(new CreateShoeRequest("Daily", new string('b', 101)).Validate());
    }
}

public class ShoeTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Only_own_active_shoes_can_be_used()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var owner = Guid.NewGuid();
        await SeedUser(conn, owner);
        var shoe = await conn.ExecuteScalarAsync<Guid>(
            "insert into shoes (user_id, name) values (@owner, 'Daily') returning id", new { owner });
        Assert.True(await ShoeEndpoints.CanUseAsync(conn, owner, null));
        Assert.True(await ShoeEndpoints.CanUseAsync(conn, owner, shoe));
        Assert.False(await ShoeEndpoints.CanUseAsync(conn, Guid.NewGuid(), shoe));
        Assert.False(await ShoeEndpoints.CanUseAsync(conn, owner, Guid.NewGuid()));
        await conn.ExecuteAsync("update shoes set retired = true where id = @shoe", new { shoe });
        Assert.False(await ShoeEndpoints.CanUseAsync(conn, owner, shoe));
        // Transaktionen återställs; testet använder bara direkt databasförberedelse.
    }

    [Fact]
    public async Task Shoes_are_private_and_retirement_and_deletion_keep_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        try
        {
            await SeedUser(conn, owner);
            await SeedUser(conn, other);
            using var client = await SignIn(owner, ct);
            using var stranger = await SignIn(other, ct);
            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/shoes", ct)).StatusCode);
            var created = await client.PostAsJsonAsync("/api/shoes", new CreateShoeRequest(" Daily ", " Brand "), ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var shoe = (await created.Content.ReadFromJsonAsync<Shoe>(ct))!;
            Assert.Equal(("Daily", "Brand", false), (shoe.Name, shoe.Brand, shoe.Retired));
            Assert.Empty((await stranger.GetFromJsonAsync<ShoeList>("/api/shoes", ct))!.Shoes);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsync($"/api/shoes/{shoe.Id}/retire", null, ct)).StatusCode);
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, shoe_id)
                values (@owner, 'manual', now() - interval '1 day', 1800, 5000, @id),
                       (@owner, 'manual', now(), 1800, 5000, null)
                """, new { owner, shoe.Id });
            var list = (await client.GetFromJsonAsync<ShoeList>("/api/shoes", ct))!;
            Assert.Equal(shoe.Id, list.LastUsedShoeId);
            Assert.Single(list.Shoes);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/shoes/{shoe.Id}/retire", null, ct)).StatusCode);
            list = (await client.GetFromJsonAsync<ShoeList>("/api/shoes", ct))!;
            Assert.True(Assert.Single(list.Shoes).Retired);
            Assert.Null(list.LastUsedShoeId);
            Assert.Equal(shoe.Id, await conn.QuerySingleAsync<Guid>(
                "select shoe_id from activities where user_id = @owner and shoe_id is not null", new { owner }));
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/shoes/{shoe.Id}", ct)).StatusCode);
            Assert.True(await conn.ExecuteScalarAsync<bool>("select exists(select 1 from shoes where id = @id)", new { shoe.Id }));
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/shoes/{shoe.Id}", ct)).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<ShoeList>("/api/shoes", ct))!.Shoes);
            Assert.Equal(2, await conn.ExecuteScalarAsync<int>(
                "select count(*) from activities where user_id = @owner and shoe_id is null", new { owner }));
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/shoes/{shoe.Id}", ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@owner, @other)", new { owner, other });
        }
    }

    [Fact]
    public async Task Manual_and_fit_creation_store_shoe_and_reject_unavailable_selection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        try
        {
            await SeedUser(conn, owner);
            await SeedUser(conn, other);
            using var client = await SignIn(owner, ct);
            var shoeId = await conn.ExecuteScalarAsync<Guid>(
                "insert into shoes (user_id, name) values (@owner, 'Daily') returning id", new { owner });
            var foreignShoe = await conn.ExecuteScalarAsync<Guid>(
                "insert into shoes (user_id, name) values (@other, 'Private') returning id", new { other });
            var run = new ManualActivityRequest("running", null, DateTimeOffset.UtcNow.AddHours(-1), 1800, 5000, null, null, shoeId);
            var manual = await client.PostAsJsonAsync("/api/activities/manual", run, ct);
            Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
            var activity = (await manual.Content.ReadFromJsonAsync<ActivitySummary>(ct))!;
            Assert.Equal(shoeId, activity.ShoeId);
            Assert.Equal(shoeId, await conn.QuerySingleAsync<Guid>("select shoe_id from activities where id = @id", new { activity.Id }));
            var noShoe = await client.PostAsJsonAsync("/api/activities/manual", run with { ShoeId = null }, ct);
            Assert.Null((await noShoe.Content.ReadFromJsonAsync<ActivitySummary>(ct))!.ShoeId);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/activities/manual", run with { ShoeId = foreignShoe }, ct)).StatusCode);

            var bytes = FitFile.Run(DateTime.UtcNow.AddHours(-2), 60);
            async Task<HttpResponseMessage> Upload(Guid selected)
            {
                using var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(bytes), "file", "synthetic.fit");
                form.Add(new StringContent(selected.ToString()), "shoeId");
                return await client.PostAsync("/api/activities", form, ct);
            }
            var fit = await Upload(shoeId);
            Assert.Equal(HttpStatusCode.Created, fit.StatusCode);
            activity = (await fit.Content.ReadFromJsonAsync<ActivitySummary>(ct))!;
            Assert.Equal(shoeId, activity.ShoeId);
            Assert.Equal(shoeId, await conn.QuerySingleAsync<Guid>("select shoe_id from activities where id = @id", new { activity.Id }));
            var rejected = await Upload(foreignShoe);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal("unavailableShoe", (await rejected.Content.ReadFromJsonAsync<ApiError>(ct))!.Code);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id in (@owner, @other)", new { owner, other });
        }
    }

    static async Task SeedUser(NpgsqlConnection conn, Guid id)
    {
        await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)", new { id, email = $"shoe-{id:N}@example.com" });
        await conn.ExecuteAsync("insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
    }

    async Task<HttpClient> SignIn(Guid id, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await SupabaseCookieSession.SignInAsync(http, new AuthSession(id, $"shoe-{id:N}@example.com",
            "synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddHours(1)), ct);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", http.Response.Headers.SetCookie.Select(c => c!.Split(';')[0])));
        await TestSession.UseCsrfToken(client, ct);
        return client;
    }
}
