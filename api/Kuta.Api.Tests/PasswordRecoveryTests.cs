using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class PasswordRecoveryTests
{
    const string Password = "old-test-password-42";
    const string NewPassword = "new-test-password-73";

    [Fact]
    public async Task Recovery_is_scoped_single_use_and_revokes_existing_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        var client = Client(factory);
        var email = $"recovery-{Guid.NewGuid():N}@example.com";
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await Csrf(client, ct);
            Assert.Equal(HttpStatusCode.Accepted,
                (await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password }, ct)).StatusCode);
            var signupHash = await EmailToken(email, "confirm", ct);
            var confirm = await client.GetAsync($"/api/auth/confirm?token_hash={signupHash}", ct);
            Assert.Equal("/", confirm.Headers.Location?.ToString());
            var originalCookies = confirm.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]);
            var originalSession = Client(factory, cookies: false);
            originalSession.DefaultRequestHeaders.Add("Cookie", string.Join("; ", originalCookies));
            Assert.Equal(HttpStatusCode.OK, (await originalSession.GetAsync("/api/me", ct)).StatusCode);

            // Mejllänken kan öppnas i en annan webbläsare utan att logga in i appen.
            var recovery = Client(factory);
            await Csrf(recovery, ct);
            Assert.Equal(HttpStatusCode.Accepted,
                (await recovery.PostAsJsonAsync("/api/auth/recover", new { email }, ct)).StatusCode);
            var hash = await EmailToken(email, "recovery", ct);
            var link = await recovery.GetAsync($"/api/auth/reset-password?token_hash={hash}", ct);
            Assert.Equal("/?reset=password", link.Headers.Location?.ToString());
            Assert.Equal("no-store", link.Headers.CacheControl?.ToString());
            var cookie = Assert.Single(link.Headers.GetValues("Set-Cookie"), c => c.StartsWith("kuta.recovery="));
            Assert.Contains("httponly", cookie.ToLowerInvariant());
            Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
            Assert.Equal(HttpStatusCode.Unauthorized, (await recovery.GetAsync("/api/me", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await recovery.GetAsync("/api/activities", ct)).StatusCode);

            // Testa en stulen kopia med en egen giltig CSRF-token.
            var replay = Client(factory, cookies: false);
            var replayCsrf = await replay.GetAsync("/api/antiforgery", ct);
            var replayToken = (await replayCsrf.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("token").GetString();
            replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0] + "; " +
                string.Join("; ", replayCsrf.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
            replay.DefaultRequestHeaders.Add("X-CSRF-TOKEN", replayToken);

            Assert.Equal(HttpStatusCode.BadRequest,
                (await recovery.PostAsJsonAsync("/api/auth/reset-password", new { password = Password }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await recovery.PostAsJsonAsync("/api/auth/reset-password", new { password = NewPassword }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await originalSession.GetAsync("/api/me", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await replay.PostAsJsonAsync("/api/auth/reset-password", new { password = "another-password-94" }, ct)).StatusCode);
            Assert.Equal("/?reset=failed",
                (await recovery.GetAsync($"/api/auth/reset-password?token_hash={hash}", ct)).Headers.Location?.ToString());
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await recovery.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await recovery.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword }, ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }
    }

    [Fact]
    public async Task Expired_recovery_and_normal_login_cannot_reset_password()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        var client = Client(factory);
        var email = $"expired-recovery-{Guid.NewGuid():N}@example.com";
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await Csrf(client, ct);
            Assert.Equal(HttpStatusCode.Accepted,
                (await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password }, ct)).StatusCode);
            var signupHash = await EmailToken(email, "confirm", ct);
            Assert.Equal("/", (await client.GetAsync($"/api/auth/confirm?token_hash={signupHash}", ct)).Headers.Location?.ToString());
            await Csrf(client, ct);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/reset-password", new { password = NewPassword }, ct)).StatusCode);

            var recovery = Client(factory);
            await Csrf(recovery, ct);
            Assert.Equal(HttpStatusCode.Accepted,
                (await recovery.PostAsJsonAsync("/api/auth/recover", new { email }, ct)).StatusCode);
            var hash = await EmailToken(email, "recovery", ct);
            Assert.Equal("/?reset=password",
                (await recovery.GetAsync($"/api/auth/reset-password?token_hash={hash}", ct)).Headers.Location?.ToString());
            await conn.ExecuteAsync("""
                update user_sessions set expires_at = now() - interval '1 second'
                where user_id = (select id from auth.users where email = @email)
                """, new { email });
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await recovery.PostAsJsonAsync("/api/auth/reset-password", new { password = NewPassword }, ct)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }
    }

    [Fact]
    public async Task Recovery_requires_csrf_and_a_valid_link_and_hides_unknown_accounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        var client = Client(factory);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/recover", new { email = "nobody@example.com" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new { password = NewPassword }, ct)).StatusCode);
        await Csrf(client, ct);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/recover", new { email = "invalid" }, ct)).StatusCode);
        var unknown = await client.PostAsJsonAsync("/api/auth/recover", new { email = $"unknown-{Guid.NewGuid():N}@example.com" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Contains("Om adressen har ett konto", await unknown.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new { password = "short" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new { password = NewPassword }, ct)).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", "kuta.recovery=tampered");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new { password = NewPassword }, ct)).StatusCode);
        Assert.Equal("/?reset=failed", (await client.GetAsync("/api/auth/reset-password", ct)).Headers.Location?.ToString());
        Assert.Equal("/?reset=failed", (await client.GetAsync("/api/auth/reset-password?token_hash=invalid", ct)).Headers.Location?.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_reset_revokes_sessions_even_when_provider_result_is_unknown(bool loseResponse)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await db.OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        await conn.ExecuteAsync("insert into auth.users (id, email) values (@userId, @email)",
            new { userId, email = $"cancelled-reset-{userId:N}@example.com" });
        try
        {
            await conn.ExecuteAsync("insert into user_sessions (user_id, expires_at) values (@userId, now() + interval '1 day')",
                new { userId });
            var start = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            await PasswordRecovery.StartAsync(start,
                new AuthSession(userId, "synthetic@example.com", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)), ct);
            using var aborted = new CancellationTokenSource();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = aborted.Token };
            http.Request.Headers.Cookie = start.Response.Headers.SetCookie.ToString().Split(';')[0];
            using var handler = new CancelAfterPasswordChange(aborted, loseResponse);
            using var transport = new HttpClient(handler);
            var supabase = new SupabaseAuthClient(transport,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(), new HttpContextAccessor());

            var error = await Record.ExceptionAsync(() => PasswordRecovery.ResetAsync(NewPassword, http, supabase, db, aborted.Token));

            Assert.True(handler.PasswordChanged);
            Assert.True(error is null or OperationCanceledException, error?.ToString());
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
                "select count(*) from user_sessions where user_id = @userId and ended_at is null", new { userId }));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId });
        }
    }

    sealed class CancelAfterPasswordChange(CancellationTokenSource aborted, bool loseResponse) : HttpMessageHandler
    {
        public bool PasswordChanged { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Put)
            {
                PasswordChanged = true;
                aborted.Cancel();
                if (loseResponse) throw new TaskCanceledException("Provider changed the password, but its response was lost.");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    static HttpClient Client(WebApplicationFactory<Program> factory, bool cookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = cookies });

    static async Task Csrf(HttpClient client, CancellationToken ct)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery", ct);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }

    static async Task<string> EmailToken(string email, string type, CancellationToken ct)
    {
        using var mailpit = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:55324") };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var search = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{email}", ct);
            foreach (var message in search.GetProperty("messages").EnumerateArray())
            {
                var full = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/message/{message.GetProperty("ID").GetString()}", ct);
                var html = full.GetProperty("HTML").GetString()!;
                // En lånad standardinstans kan fortfarande ha Supabases standardmall för recovery.
                if (type == "recovery" && !html.Contains("reset-password") && !html.Contains("type=recovery")) continue;
                if (type == "confirm" && !html.Contains("/api/auth/confirm")) continue;
                var match = Regex.Match(html, "(?:token_hash|token)=([0-9a-f]+)");
                if (match.Success) return match.Groups[1].Value;
            }
            await Task.Delay(250, ct);
        }
        throw new TimeoutException($"Inget {type}-mejl till {email} i Mailpit.");
    }
}
