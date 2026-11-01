using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static TestSession;

// Requires local Supabase (see KutaFactory): the account is really created in Supabase Auth and
// the confirmation email is read from Mailpit.
public class AuthTests(KutaFactory factory) : IClassFixture<KutaFactory>
{

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
            var withoutTerms = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, withoutTerms.StatusCode);
            Assert.Equal("termsRequired", (await withoutTerms.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());

            var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password, acceptTerms = true }, ct);
            Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);

            var beforeConfirm = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.Forbidden, beforeConfirm.StatusCode);

            var tokenHash = await ConfirmationTokenHash(email, ct);
            // The link only confirms the address: whoever clicks it is not signed in to the account (login CSRF)
            var confirm = await client.GetAsync($"/api/auth/confirm?token_hash={tokenHash}", ct);
            Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
            Assert.Equal("/app?confirm=ok", confirm.Headers.Location?.ToString());
            Assert.False(confirm.Headers.Contains("Set-Cookie"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", ct)).StatusCode);

            var firstLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, ct);
            Assert.Equal(HttpStatusCode.NoContent, firstLogin.StatusCode);
            var sessionCookies = firstLogin.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();

            var me = await client.GetFromJsonAsync<JsonElement>("/api/me", ct);
            Assert.Equal(email, me.GetProperty("email").GetString());
            Assert.Equal(JsonValueKind.Null, me.GetProperty("displayName").ValueKind);

            // The antiforgery token is bound to the user and must be fetched again after login
            await UseCsrfToken(client, ct);
            var put = await client.PutAsJsonAsync("/api/me", new { displayName = "Testlöpare" }, ct);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.Equal(AuthEndpoints.TermsVersion, await conn.ExecuteScalarAsync<string>(
                "select terms_version from users where id = @id and terms_accepted_at is not null",
                new { id = me.GetProperty("id").GetGuid() }));

            await conn.ExecuteAsync(
                "insert into activities (user_id, source, started_at, duration_s, distance_m) values (@id, 'manual', now(), 1800, 5000)",
                new { id = me.GetProperty("id").GetGuid() });
            var activities = await client.GetStringAsync("/api/activities", ct);
            Assert.Contains("\"startedAt\"", activities);
            Assert.Contains("\"distanceM\":5000", activities);

            var reused = await client.GetAsync($"/api/auth/confirm?token_hash={tokenHash}", ct);
            Assert.Equal("/app?confirm=failed", reused.Headers.Location?.ToString());

            var replay = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            replay.DefaultRequestHeaders.Add("Cookie", string.Join("; ", sessionCookies));
            Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/me", ct)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", ct)).StatusCode);
            // A copy of the old cookie does not work either: the session is ended in the database
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
            // Cascades to users, user_sessions and activities
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }
    }
}
