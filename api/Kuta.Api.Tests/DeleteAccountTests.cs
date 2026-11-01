using System.Net;
using System.Net.Http.Json;
using Amazon.S3;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class DeleteAccountTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    const string Password = "synthetic-pass-1";

    [Fact]
    public async Task Delete_requires_auth_and_csrf_removes_owned_data_and_invalidates_all_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var email = $"delete-{Guid.NewGuid():N}@example.com";
        await TestSession.CreateRunnerAsync(factory, owner, conn, email, "Delete runner", ct);
        await TestSession.CreateRunnerAsync(factory, other, conn, $"other-{Guid.NewGuid():N}@example.com", "Other runner", ct);
        var ownerId = await conn.ExecuteScalarAsync<Guid>("select id from auth.users where email = @email", new { email });
        var otherId = (await other.GetFromJsonAsync<Me>("/api/me", ct))!.Id;
        var store = factory.Services.GetRequiredService<RawFileStore>();
        var key = $"fit/{ownerId}/synthetic.fit";
        var otherKey = $"fit/{otherId}/synthetic.fit";
        try
        {
            foreach (var (id, fileKey) in new[] { (ownerId, key), (otherId, otherKey) })
            {
                await conn.ExecuteAsync("""
                    insert into equipment (id, user_id, name) values (@id, @id, 'Synthetic shoe');
                    insert into activities (id, user_id, source, started_at, duration_s, distance_m, raw_file_key, avg_hr)
                    values (@id, @id, 'fit', now(), 600, 1800, @fileKey, 150);
                    insert into activity_equipment values (@id, @id, @id);
                    insert into activity_streams (activity_id, t, hr) values (@id, array[0,600], array[150,160]);
                    insert into activity_laps (activity_id, lap_index, start_s, duration_s, distance_m) values (@id, 0, 0, 600, 1800);
                    insert into activity_parts values (@id, true, 0, 'running', 0, 600, 1800, true);
                    insert into best_efforts (activity_id, user_id, distance_m, elapsed_s, start_offset_s, achieved_at)
                    values (@id, @id, 1000, 300, 0, now());
                    insert into user_sessions (user_id, expires_at) values (@id, now() + interval '1 day');
                    insert into auth.sessions (id, user_id) values (@id, @id);
                    """, new { id, fileKey });
                await store.PutAsync(fileKey, "synthetic file"u8.ToArray(), ct);
            }
            using var oldSession = factory.CreateClient();
            oldSession.DefaultRequestHeaders.Add("Cookie", owner.DefaultRequestHeaders.GetValues("Cookie"));
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/activities/{ownerId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/activities/{ownerId}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteMe(anonymous, Password)).StatusCode);
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await DeleteMe(owner, Password)).StatusCode);
            await TestSession.UseCsrfToken(owner, ct);
            // The session alone is not enough: the current password must be given.
            await TestSession.SetPasswordAsync(conn, email, Password);
            using (var wrong = await DeleteMe(owner, "wrong-password-1"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
                Assert.Contains("wrongPassword", await wrong.Content.ReadAsStringAsync(ct));
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await DeleteMe(owner, "")).StatusCode);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from auth.users where id = @ownerId", new { ownerId }));
            using var response = await DeleteMe(owner, Password);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("kuta.session=;"));
            foreach (var table in new[] { "auth.users", "users", "activities", "equipment" })
                Assert.Equal(0, await conn.ExecuteScalarAsync<int>($"select count(*) from {table} where id = @ownerId", new { ownerId }));
            foreach (var table in new[] { "auth.sessions", "user_sessions", "best_efforts", "activity_equipment" })
                Assert.Equal(0, await conn.ExecuteScalarAsync<int>($"select count(*) from {table} where user_id = @ownerId", new { ownerId }));
            foreach (var table in new[] { "activity_streams", "activity_laps", "activity_parts" })
                Assert.Equal(0, await conn.ExecuteScalarAsync<int>($"select count(*) from {table} where activity_id = @ownerId", new { ownerId }));
            Assert.Equal(0, await Files(key));
            Assert.Equal(1, await Files(otherKey));
            Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/me", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await other.GetAsync($"/api/activities/{otherId}", ct)).StatusCode);
        }
        finally
        {
            await store.DeleteAsync([key, otherKey], ct);
            await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { ownerId, otherId } });
        }
        Task<HttpResponseMessage> DeleteMe(HttpClient client, string password) => client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { password }) }, ct);
        Task<int> Files(string name) => conn.ExecuteScalarAsync<int>(
            "select count(*) from storage.objects where bucket_id = 'fit-files' and name = @name", new { name });
    }

    [Fact]
    public async Task Password_check_shares_the_login_limit_per_email()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var client = factory.CreateClient();
        var email = $"delete-limit-{Guid.NewGuid():N}@example.com";
        await TestSession.CreateRunnerAsync(factory, client, conn, email, "Delete limit runner", ct);
        try
        {
            await TestSession.SetPasswordAsync(conn, email, Password);
            while (AuthEndpoints.LimitPerEmail(email) is null) { }
            using var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { password = Password }) }, ct);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from auth.users where email = @email", new { email }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }

    [Fact]
    public async Task Account_without_a_profile_can_be_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("insert into auth.users (id) values (@id)", new { id });
        try
        {
            await MeEndpoints.DeleteAccountAsync(conn, factory.Services.GetRequiredService<RawFileStore>(), id, ct);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("select count(*) from auth.users where id = @id", new { id }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @id", new { id }); }
    }

    [Fact]
    public async Task Storage_failure_preserves_account_and_file_references_for_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var id = Guid.NewGuid();
        var configuration = new ConfigurationBuilder().AddConfiguration(factory.Services.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:AccessKeyId"] = "synthetic-invalid-key" }).Build();
        await conn.ExecuteAsync("""
            insert into auth.users (id) values (@id);
            insert into users (id, display_name) values (@id, 'Synthetic runner');
            insert into activities (user_id, source, started_at, duration_s, distance_m, raw_file_key)
            values (@id, 'fit', now(), 600, 1800, 'synthetic-failed-delete.fit')
            """, new { id });
        try
        {
            await Assert.ThrowsAsync<AmazonS3Exception>(() => MeEndpoints.DeleteAccountAsync(conn, new RawFileStore(configuration), id, ct));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from auth.users where id = @id", new { id }));
            Assert.Equal("synthetic-failed-delete.fit", await conn.QuerySingleAsync<string>("select raw_file_key from activities where user_id = @id", new { id }));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @id", new { id }); }
    }

    [Fact]
    public async Task Delete_waits_for_import_and_includes_its_committed_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = factory.Services.GetRequiredService<NpgsqlDataSource>();
        var store = factory.Services.GetRequiredService<RawFileStore>();
        await using var import = await db.OpenConnectionAsync(ct);
        await using var deletion = await db.OpenConnectionAsync(ct);
        var id = Guid.NewGuid();
        var key = $"fit/{id}/concurrent.fit";
        await import.ExecuteAsync("insert into auth.users (id) values (@id); insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
        try
        {
            Task deleting;
            await using (var tx = await import.BeginTransactionAsync(ct))
            {
                await import.ExecuteAsync("select id from users where id = @id for update", new { id }, tx);
                deleting = MeEndpoints.DeleteAccountAsync(deletion, store, id, ct);
                await DatabaseWait.UntilBlockedAsync(import, deletion.ProcessID, ct);
                await import.ExecuteAsync("""
                    insert into activities (user_id, source, started_at, duration_s, distance_m, raw_file_key)
                    values (@id, 'fit', now(), 600, 1800, @key)
                    """, new { id, key }, tx);
                await store.PutAsync(key, "synthetic file"u8.ToArray(), ct);
                await tx.CommitAsync(ct);
            }
            await deleting;
            Assert.Equal(0, await import.ExecuteScalarAsync<int>("select count(*) from storage.objects where name = @key", new { key }));
            Assert.Equal(0, await import.ExecuteScalarAsync<int>("select count(*) from auth.users where id = @id", new { id }));
        }
        finally
        {
            await store.DeleteAsync([key], ct);
            await import.ExecuteAsync("delete from auth.users where id = @id", new { id });
        }
    }
}
