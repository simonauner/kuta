using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class PrivacyPlacesTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public void Validation_covers_names_coordinates_and_radius_boundaries()
    {
        var valid = new PrivacyPlaceRequest(" Home ", -90, 180, 50);
        Assert.Null(PrivacyPlaces.Validate(valid));
        Assert.Null(PrivacyPlaces.Validate(valid with { Latitude = 90, Longitude = -180, RadiusM = 5000 }));
        foreach (var invalid in new[] {
            valid with { Name = " " }, valid with { Name = new string('a', 101) },
            valid with { Latitude = null }, valid with { Longitude = null },
            valid with { Latitude = double.NaN }, valid with { Latitude = -90.01 },
            valid with { Longitude = double.PositiveInfinity }, valid with { Longitude = 180.01 },
            valid with { RadiusM = 49 }, valid with { RadiusM = 5001 } })
            Assert.Equal("invalidPrivacyPlace", PrivacyPlaces.Validate(invalid)!.Code);
    }

    [Fact]
    public async Task Storage_is_owner_scoped_and_deleting_account_cascades_places()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        try
        {
            foreach (var id in ids)
                await conn.ExecuteAsync("""
                    insert into auth.users(id, email) values (@id, @email);
                    insert into users(id, display_name) values (@id, 'Synthetic runner')
                    """, new { id, email = $"privacy-{id:N}@example.com" });
            var home = await PrivacyPlaces.CreateAsync(conn, ids[0], new PrivacyPlaceRequest(" Home ", 59, 18), ct);
            var work = await PrivacyPlaces.CreateAsync(conn, ids[0], new PrivacyPlaceRequest("Work", 60, 19, 500), ct);
            var other = await PrivacyPlaces.CreateAsync(conn, ids[1], new PrivacyPlaceRequest("Other home", 58, 17), ct);
            Assert.Equal("Home", home.Name);
            Assert.Equal(200, home.RadiusM);
            Assert.Equal(2, (await PrivacyPlaces.LoadAsync(conn, ids[0], ct)).Length);
            Assert.Equal(other.Id, Assert.Single(await PrivacyPlaces.LoadAsync(conn, ids[1], ct)).Id);
            var update = new PrivacyPlaceRequest("New home", 57, 16, 1000);
            Assert.False(await PrivacyPlaces.UpdateAsync(conn, ids[1], home.Id, update, ct));
            Assert.False(await PrivacyPlaces.DeleteAsync(conn, ids[1], home.Id, ct));
            Assert.True(await PrivacyPlaces.UpdateAsync(conn, ids[0], home.Id, update, ct));
            var updated = (await PrivacyPlaces.LoadAsync(conn, ids[0], ct)).Single(p => p.Id == home.Id);
            Assert.Equal(("New home", 57d, 16d, 1000), (updated.Name, updated.Latitude, updated.Longitude, updated.RadiusM));
            Assert.True(await PrivacyPlaces.DeleteAsync(conn, ids[0], work.Id, ct));

            var invalid = await Assert.ThrowsAsync<PostgresException>(() => PrivacyPlaces.CreateAsync(conn, ids[0], update with { RadiusM = 0 }, ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
            Assert.True(await conn.ExecuteScalarAsync<bool>("select relrowsecurity from pg_class where oid = 'privacy_places'::regclass"));
            foreach (var role in new[] { "anon", "authenticated" })
                Assert.False(await conn.ExecuteScalarAsync<bool>("select has_table_privilege(@role, 'privacy_places', 'SELECT,INSERT,UPDATE,DELETE')", new { role }));
            await conn.ExecuteAsync("delete from auth.users where id = @id", new { id = ids[0] });
            Assert.Empty(await PrivacyPlaces.LoadAsync(conn, ids[0], ct));
            Assert.Single(await PrivacyPlaces.LoadAsync(conn, ids[1], ct));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids }); }
    }

    [Fact]
    public async Task Http_crud_uses_session_owner_and_never_exposes_another_users_place()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var emails = new[] { $"place-owner-{Guid.NewGuid():N}@example.com", $"place-other-{Guid.NewGuid():N}@example.com" };
        const string path = "/api/me/privacy-places";
        try
        {
            await TestSession.CreateRunnerAsync(factory, owner, conn, emails[0], "Owner", ct);
            await TestSession.CreateRunnerAsync(factory, other, conn, emails[1], "Other", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
            var request = new PrivacyPlaceRequest("Home", 59, 18);
            using var created = await owner.PostAsJsonAsync(path, request, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var place = (await created.Content.ReadFromJsonAsync<PrivacyPlace>(ct))!;
            Assert.Equal(place.Id, Assert.Single((await owner.GetFromJsonAsync<PrivacyPlace[]>(path, ct))!).Id);
            using var list = await other.GetAsync(path, ct);
            Assert.True(list.Headers.CacheControl!.NoStore);
            Assert.Empty((await list.Content.ReadFromJsonAsync<PrivacyPlace[]>(ct))!);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"{path}/{place.Id}", request, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{path}/{place.Id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{path}/{place.Id}", request with { Name = "Work", RadiusM = 500 }, ct)).StatusCode);
            Assert.Equal("Work", Assert.Single((await owner.GetFromJsonAsync<PrivacyPlace[]>(path, ct))!).Name);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { name = "Missing map point" }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{path}/{place.Id}", ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<PrivacyPlace[]>(path, ct))!);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = any(@emails)", new { emails }); }
    }
}
