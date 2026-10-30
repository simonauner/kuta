using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

// Kräver lokal Supabase (`supabase start`): kontot skapas på riktigt i Supabase Auth och
// bekräftelsemejlet läses från Mailpit.
public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    const string MailpitUrl = "http://127.0.0.1:55324";
    const string Password = "hemligt-lösen";

    [Fact]
    public async Task Signup_confirm_logout_and_login()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", ct)).StatusCode);

            var withoutCsrf = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

            await UseCsrfToken(client, ct);
            var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);

            var beforeConfirm = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.Forbidden, beforeConfirm.StatusCode);

            var tokenHash = await ConfirmationTokenHash(email, ct);
            var confirm = await client.GetAsync($"/api/auth/confirm?token_hash={tokenHash}", ct);
            Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
            Assert.Equal("/", confirm.Headers.Location?.ToString());
            var sessionCookies = confirm.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();

            var me = await client.GetFromJsonAsync<JsonElement>("/api/me", ct);
            Assert.Equal(email, me.GetProperty("email").GetString());
            Assert.Equal(JsonValueKind.Null, me.GetProperty("displayName").ValueKind);

            // Antiforgery-token är knuten till användaren och måste hämtas om efter inloggning
            await UseCsrfToken(client, ct);
            var put = await client.PutAsJsonAsync("/api/me", new { displayName = "Testlöpare" }, ct);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            await conn.ExecuteAsync(
                "insert into activities (user_id, source, started_at, duration_s, distance_m) values (@id, 'manual', now(), 1800, 5000)",
                new { id = me.GetProperty("id").GetGuid() });
            var activities = await client.GetStringAsync("/api/activities", ct);
            Assert.Contains("\"startedAt\"", activities);
            Assert.Contains("\"distanceM\":5000", activities);

            var reused = await client.GetAsync($"/api/auth/confirm?token_hash={tokenHash}", ct);
            Assert.Equal("/?confirm=failed", reused.Headers.Location?.ToString());

            var replay = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            replay.DefaultRequestHeaders.Add("Cookie", string.Join("; ", sessionCookies));
            Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/me", ct)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", ct)).StatusCode);
            // En kopia av den gamla cookien fungerar inte heller: sessionen är avslutad i databasen
            Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/me", ct)).StatusCode);

            await UseCsrfToken(client, ct);
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "fel-lösen" }, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            me = await client.GetFromJsonAsync<JsonElement>("/api/me", ct);
            Assert.Equal("Testlöpare", me.GetProperty("displayName").GetString());
        }
        finally
        {
            // Kaskaderar till users, user_sessions och activities
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }
    }

    static async Task UseCsrfToken(HttpClient client, CancellationToken ct)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery", ct);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }

    static async Task<string> ConfirmationTokenHash(string email, CancellationToken ct)
    {
        using var mailpit = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var search = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{email}", ct);
            if (search.GetProperty("messages").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } message)
            {
                var full = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/message/{message.GetProperty("ID").GetString()}", ct);
                var match = Regex.Match(full.GetProperty("HTML").GetString()!, "token_hash=([0-9a-f]+)");
                Assert.True(match.Success,
                    "Mejlet saknar vår länk. Supabase har laddat standardmallen; kör `docker restart supabase_auth_kuta`.");
                return match.Groups[1].Value;
            }
            await Task.Delay(250, ct);
        }
        throw new TimeoutException($"Inget bekräftelsemejl till {email} i Mailpit.");
    }
}
