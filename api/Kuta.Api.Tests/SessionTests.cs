using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

public class SessionTests
{
    [Fact]
    public async Task Refresh_replaces_both_tokens_and_renews_the_cookie()
    {
        await using var session = await SessionCase.CreateAsync();
        session.AccessExpiresIn(TimeSpan.FromMinutes(1));

        await SupabaseCookieSession.ValidatePrincipalAsync(session.Context);

        Assert.NotNull(session.Context.Principal);
        Assert.True(session.Context.ShouldRenew);
        Assert.Equal("new-access", session.Context.Properties.GetTokenValue("supabase_access_token"));
        Assert.Equal("new-refresh", session.Context.Properties.GetTokenValue("supabase_refresh_token"));
        Assert.Equal(1, session.Handler.Calls);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("other-user")]
    public async Task Invalid_refresh_rejects_the_session(string response)
    {
        await using var session = await SessionCase.CreateAsync(response);
        session.AccessExpiresIn(TimeSpan.FromMinutes(1));

        await SupabaseCookieSession.ValidatePrincipalAsync(session.Context);

        Assert.Null(session.Context.Principal);
        Assert.Contains("kuta.session=;", session.Context.Response.Headers.SetCookie.ToString());
    }

    [Theory]
    [InlineData("unavailable", true)]
    [InlineData("unavailable", false)]
    [InlineData("timeout", true)]
    [InlineData("timeout", false)]
    [InlineData("malformed", true)]
    [InlineData("malformed", false)]
    public async Task Refresh_outage_keeps_only_an_unexpired_access_token(string response, bool unexpired)
    {
        await using var session = await SessionCase.CreateAsync(response);
        session.AccessExpiresIn(TimeSpan.FromMinutes(unexpired ? 1 : -1));

        await SupabaseCookieSession.ValidatePrincipalAsync(session.Context);

        Assert.Equal(unexpired, session.Context.Principal is not null);
        Assert.False(session.Context.ShouldRenew);
    }

    [Fact]
    public async Task Concurrent_refreshes_and_a_late_old_cookie_share_one_result()
    {
        await using var session = await SessionCase.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Handler.BeforeResponse = async () =>
        {
            started.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        };
        var refresh = new SharedTokenRefresh();
        var auth = session.Context.HttpContext.RequestServices.GetRequiredService<SupabaseAuthClient>();
        var first = refresh.RefreshAsync(auth, "same-old-refresh");
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = refresh.RefreshAsync(auth, "same-old-refresh");
        Assert.Equal(1, session.Handler.Calls);
        release.SetResult();

        var results = await Task.WhenAll(first, second);
        var late = await refresh.RefreshAsync(auth, "same-old-refresh");

        Assert.NotNull(results[0]);
        Assert.Same(results[0], results[1]);
        Assert.Same(results[0], late);
        Assert.Equal(1, session.Handler.Calls);
    }

    [Fact]
    public async Task Failed_refresh_can_be_retried()
    {
        await using var session = await SessionCase.CreateAsync("unavailable");
        var refresh = new SharedTokenRefresh();
        var auth = session.Context.HttpContext.RequestServices.GetRequiredService<SupabaseAuthClient>();
        await Assert.ThrowsAsync<HttpRequestException>(() => refresh.RefreshAsync(auth, "old-refresh"));
        session.Handler.Response = "ok";

        var result = await refresh.RefreshAsync(auth, "old-refresh");

        Assert.NotNull(result);
        Assert.Equal(2, session.Handler.Calls);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("rejected", false)]
    [InlineData("other-user", false)]
    [InlineData("unavailable", true)]
    public async Task Provider_validation_runs_after_five_minutes(string response, bool accepted)
    {
        await using var session = await SessionCase.CreateAsync(response);
        // A fresh cookie does not need a provider check.
        await SupabaseCookieSession.ValidatePrincipalAsync(session.Context);
        Assert.Equal(0, session.Handler.Calls);
        session.Context.Properties.Items[".kuta.validated"] = DateTimeOffset.UtcNow.AddMinutes(-6).ToString("O");

        await SupabaseCookieSession.ValidatePrincipalAsync(session.Context);

        Assert.Equal(accepted, session.Context.Principal is not null);
        Assert.Equal(response == "ok", session.Context.ShouldRenew);
        Assert.Equal(1, session.Handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ended_or_absolutely_expired_sessions_are_rejected_before_contacting_provider(bool expired)
    {
        await using var session = await SessionCase.CreateAsync();
        await using var conn = await session.Db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await conn.ExecuteAsync(expired
            ? "update user_sessions set expires_at = now() - interval '1 second' where user_id = @userId"
            : "update user_sessions set ended_at = now() where user_id = @userId", new { session.UserId });

        await SupabaseCookieSession.ValidatePrincipalAsync(session.Context);

        Assert.Null(session.Context.Principal);
        Assert.Equal(0, session.Handler.Calls);
    }

    sealed class SessionCase(WebApplicationFactory<Program> factory, IServiceScope scope,
        Guid userId, ProviderHandler handler, CookieValidatePrincipalContext context) : IAsyncDisposable
    {
        public Guid UserId => userId;
        public ProviderHandler Handler => handler;
        public CookieValidatePrincipalContext Context => context;
        public NpgsqlDataSource Db => scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        public void AccessExpiresIn(TimeSpan remaining) => context.Properties.Items[".kuta.access_expires"] =
            DateTimeOffset.UtcNow.Add(remaining).ToString("O", CultureInfo.InvariantCulture);

        public static async Task<SessionCase> CreateAsync(string response = "ok")
        {
            var ct = TestContext.Current.CancellationToken;
            var id = Guid.NewGuid();
            var handler = new ProviderHandler(id) { Response = response };
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddHttpClient<SupabaseAuthClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler)));
            var scope = factory.Services.CreateScope();
            await using var conn = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
            await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)",
                new { id, email = $"session-{id:N}@example.com" });
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = ct };
            await SupabaseCookieSession.SignInAsync(http,
                new AuthSession(id, $"session-{id:N}@example.com", "old-access", "old-refresh", DateTimeOffset.UtcNow.AddHours(1)), ct);
            var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var cookie = http.Response.Headers.SetCookie.ToString().Split(';')[0].Split('=', 2)[1];
            var ticket = options.TicketDataFormat.Unprotect(cookie)!;
            http.Response.Headers.Clear();
            var context = new CookieValidatePrincipalContext(http,
                new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
                options, ticket);
            return new SessionCase(factory, scope, id, handler, context);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var conn = await Db.OpenConnectionAsync();
                await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId });
            }
            finally
            {
                scope.Dispose();
                await factory.DisposeAsync();
            }
        }
    }

    sealed class ProviderHandler(Guid userId) : HttpMessageHandler
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public string Response { get; set; } = "ok";
        public Func<Task>? BeforeResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            if (BeforeResponse is { } wait) await wait();
            if (Response == "timeout") throw new TaskCanceledException("Synthetic timeout.");
            if (Response == "unavailable") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (Response == "rejected") return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (Response == "malformed") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid json") };
            var user = new
            {
                id = Response == "other-user" ? Guid.NewGuid() : userId,
                email = $"session-{userId:N}@example.com",
                email_confirmed_at = DateTimeOffset.UtcNow,
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.Method == HttpMethod.Get ? JsonContent.Create(user) : JsonContent.Create(new
                {
                    user, access_token = "new-access", refresh_token = "new-refresh", expires_in = 3600,
                }),
            };
        }
    }
}
