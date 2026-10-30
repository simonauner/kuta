using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class DeleteAllActivitiesTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Bulk_delete_requires_auth_and_csrf_and_removes_only_owned_data_including_files()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            using var owner = await CreateRunner(conn, ownerId, ct);
            using var other = await CreateRunner(conn, otherId, ct);
            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/activities", ct)).StatusCode);
            await owner.PutAsync("/api/me/hr-consent", null, ct);
            await other.PutAsync("/api/me/hr-consent", null, ct);
            var start = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
            var first = await Upload(owner, start, ct);
            var second = await Upload(owner, start.AddDays(1), ct);
            var otherRun = await Upload(other, start, ct);
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m)
                select @ownerId, 'manual', now(), 1800, 5000 from generate_series(1, 60)
                """, new { ownerId });
            Assert.Equal(2, await Files(ownerId));
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync("/api/activities", ct)).StatusCode);
            Assert.Equal(62, await conn.ExecuteScalarAsync<int>("select count(*) from activities where user_id = @ownerId", new { ownerId }));
            await TestSession.UseCsrfToken(owner, ct);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/activities", ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<ActivitySummary[]>("/api/activities", ct))!);
            Assert.Empty((await owner.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("""
                select (select count(*) from activity_streams where activity_id = any(@ids))
                     + (select count(*) from best_efforts where activity_id = any(@ids))
                """, new { ids = new[] { first.Id, second.Id } }));
            Assert.Equal(0, await Files(ownerId));
            Assert.Equal(otherRun.Id, Assert.Single((await other.GetFromJsonAsync<ActivitySummary[]>("/api/activities", ct))!).Id);
            Assert.NotEmpty((await other.GetFromJsonAsync<StoredBestEffort[]>("/api/records", ct))!);
            Assert.Equal(HttpStatusCode.OK, (await other.GetAsync($"/api/activities/{otherRun.Id}", ct)).StatusCode);
            Assert.Equal(1, await Files(otherId));
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/activities", ct)).StatusCode);
            await other.DeleteAsync("/api/activities", ct);
        }
        finally
        {
            // Clean up synthetic files even if an assertion fails.
            var keys = await conn.QueryAsync<string>("select raw_file_key from activities where user_id = any(@ids) and raw_file_key is not null",
                new { ids = new[] { ownerId, otherId } });
            await factory.Services.GetRequiredService<RawFileStore>().DeleteAsync(keys, ct);
            await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { ownerId, otherId } });
        }

        Task<int> Files(Guid id) => conn.ExecuteScalarAsync<int>(
            "select count(*) from storage.objects where bucket_id = 'fit-files' and name like @prefix",
            new { prefix = $"fit/{id}/%" });
    }

    private async Task<HttpClient> CreateRunner(NpgsqlConnection conn, Guid id, CancellationToken ct)
    {
        var email = $"best-effort-{id:N}@example.com";
        await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)", new { id, email });
        await conn.ExecuteAsync("insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await SupabaseCookieSession.SignInAsync(http, new AuthSession(id, email, "synthetic-access", "synthetic-refresh",
            DateTimeOffset.UtcNow.AddHours(1)), ct);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", http.Response.Headers.SetCookie.Select(c => c!.Split(';')[0])));
        await TestSession.UseCsrfToken(client, ct);
        return client;
    }

    private static async Task<ActivitySummary> Upload(HttpClient client, DateTime start, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(FitFile.Run(start, 2100));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "synthetic-run.fit");
        using var response = await client.PostAsync("/api/activities", form, ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<ActivitySummary>(ct))!;
    }
}
