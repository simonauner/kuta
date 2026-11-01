using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Dapper;
using DbUp;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Npgsql;

try
{
    return await RunAsync(args);
}
catch (HostAbortedException) { throw; } // WebApplicationFactory aborts startup at Build.
catch (Exception exception)
{
    PrivateLogProvider.WriteFailure(Console.Error, PrivateLogProvider.StartupFailed, exception);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // Framework logs can contain URLs, tokens and exceptions. Only reviewed diagnostic fields may leave the process.
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new PrivateLogProvider(Console.Out));

    // GSS encryption (Kerberos) off: we log in with a password, and without libgssapi in the image Npgsql logs an error on every attempt
    var connectionString = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is missing")) { GssEncryptionMode = GssEncryptionMode.Disable }
        .ConnectionString;

    // Dapper: snake_case columns -> PascalCase properties
    DefaultTypeMap.MatchNamesWithUnderscores = true;

    // Npgsql with PostGIS/NetTopologySuite
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UseNetTopologySuite();
    // Factory registration: the container then owns the pool and closes its connections when the app shuts down.
    // A ready-made instance is never disposed, so every test app would otherwise leave its connections open.
    builder.Services.AddSingleton(_ => dataSourceBuilder.Build());

    builder.Services.ConfigureHttpJsonOptions(o =>
        o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

    // Sign-in via our API against Supabase Auth (BFF); the session lives in an encrypted HttpOnly cookie
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHttpClient<SupabaseAuthClient>();
    builder.Services.AddHttpClient<TurnstileVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddHttpClient<GarminClient>();
    builder.Services.AddSingleton<SharedTokenRefresh>();
    builder.Services.AddSingleton<RawFileStore>();
    builder.Services.AddSingleton<ImportFiles>();
    builder.Services.AddHostedService<JobWorker>();

    // The keys are stored in Postgres and encrypted with a certificate outside Development (DataProtection__Certificate)
    builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>, PostgresKeyManagementSetup>();
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("kuta");
    var certificates = DataProtectionCertificates.Load(builder.Configuration, requireCurrent: !builder.Environment.IsDevelopment());
    if (certificates.Current is { } currentCertificate) dataProtection.ProtectKeysWithCertificate(currentCertificate);
    if (certificates.Previous is { } previousCertificate) dataProtection.UnprotectKeysWithAnyCertificate(previousCertificate);

    // Behind Fly's proxy (ASPNETCORE_FORWARDEDHEADERS_ENABLED): the client IP in ForwardedHeaders__ForwardedForHeaderName
    // (Fly-Client-IP) for the rate limit, and X-Forwarded-Proto so the app sees https.
    builder.Services.Configure<ForwardedHeadersOptions>(builder.Configuration.GetSection("ForwardedHeaders"));
    builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

    // __Host- requires Secure, which doesn't work over http locally
    var cookiePrefix = builder.Environment.IsDevelopment() ? "" : "__Host-";
    var cookieSecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.Cookie.Name = cookiePrefix + "kuta.session";
            o.Cookie.HttpOnly = true;
            // Lax so the link in the confirmation email opens the app signed in; writes are protected by the antiforgery token
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = cookieSecurePolicy;
            o.ExpireTimeSpan = SupabaseCookieSession.IdleTimeout;
            o.SlidingExpiration = true;
            o.Events.OnValidatePrincipal = SupabaseCookieSession.ValidatePrincipalAsync;
            // API: respond 401/403 instead of redirecting to a login page
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        });
    builder.Services.AddAuthorization();
    builder.Services.AddAntiforgery(o =>
    {
        o.HeaderName = "X-CSRF-TOKEN";
        o.Cookie.Name = cookiePrefix + "kuta.antiforgery";
        o.Cookie.SecurePolicy = cookieSecurePolicy;
    });

    // Slows password guessing and mass sign-up: 10 auth calls per 5 minutes and IP (IPv6 per /64 network).
    // Login and recovery also have a limit per email address (AuthEndpoints).
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
            AuthEndpoints.ClientPartition(ctx.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
        // File parsing costs memory and CPU: 30 uploads/previews per 5 minutes and user.
        // The group requires sign-in, and authorization runs before the rate limit.
        o.AddPolicy("uploads", ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(5) }));
        // Follow requests: 20 per hour and user, against mass requests to shared profile links.
        // Public profiles are read without an account: 120 calls per minute and IP (IPv6 per /64 network).
        o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
            AuthEndpoints.ClientPartition(ctx.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
        o.AddPolicy("follows", ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromHours(1) }));
    });

    var app = builder.Build();
    var diagnostics = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(PrivateLogProvider.Category);
    app.Lifetime.ApplicationStarted.Register(() => diagnostics.LogInformation(PrivateLogProvider.Started, ""));
    app.Lifetime.ApplicationStopped.Register(() => diagnostics.LogInformation(PrivateLogProvider.Stopped, ""));

    // The API only responds with JSON and redirects: nothing may run or load from the responses. The web CSP lives in
    // index.html (web/vite.config.ts); the header adds what a meta tag cannot, frame-ancestors.
    app.Use((context, next) =>
    {
        // OnStarting keeps the security headers even when the exception handler clears the response.
        context.Response.OnStarting(() =>
        {
            var api = context.Request.Path.StartsWithSegments("/api");
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = api ? "default-src 'none'; frame-ancestors 'none'" : "frame-ancestors 'none'";
            if (api) headers.CacheControl = "no-store";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = api ? "no-referrer" : "strict-origin-when-cross-origin";
            return Task.CompletedTask;
        });
        return next(context);
    });
    // Outer middleware also sees handled errors and errors after the response has started.
    app.Use(async (context, next) =>
    {
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;
        try { await next(context); }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            failure ??= context.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (failure is not null || context.Response.StatusCode >= 500)
                diagnostics.Log(LogLevel.Error, PrivateLogProvider.RequestFailed,
                    new PrivateLogProvider.RequestMetrics(context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds),
                    failure, (_, _) => "");
        }
    });
    // Development too should give neutral error responses without exception content.
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        SuppressDiagnosticsCallback = _ => true,
        ExceptionHandler = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiError("Ett oväntat fel inträffade.", "internalError"));
        },
    });

    if (!app.Environment.IsDevelopment()) app.UseHsts();

    // One address for the app: kuta.fly.dev and www redirect to CanonicalHost (kutaapp.com in fly.toml), so that
    // cookies, links and confirmation emails always use the same origin. Fly's health check targets the machine IP and is unaffected.
    app.Use((context, next) =>
    {
        var canonical = app.Configuration["CanonicalHost"];
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(canonical) || host == canonical ||
            !(host.EndsWith(".fly.dev", StringComparison.OrdinalIgnoreCase) || host.Equals($"www.{canonical}", StringComparison.OrdinalIgnoreCase)))
            return next(context);
        context.Response.Redirect($"https://{canonical}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}", permanent: true);
        return Task.CompletedTask;
    });

    // In production the built web app lives in wwwroot (see Dockerfile), so web and API share an origin.
    // File names under /assets contain a hash and can be cached forever; the rest (index.html, sw.js) must not be cached.
    var staticFiles = new StaticFileOptions
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
    };
    app.UseDefaultFiles();
    app.UseStaticFiles(staticFiles);
    // Explicitly in this order: otherwise WebApplication puts routing and auth first, so 401/403 lack
    // headers and the fallback route takes sw.js etc. before the static files are found.
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // Run SQL migrations (embedded in Migrations/) at startup
    var upgrader = DeployChanges.To
        .PostgresqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
        .WithTransactionPerScript()
        .LogToNowhere()
        .Build();

    // Only one process migrates at a time (parallel test starts, several machines): a Postgres session lock around DbUp,
    // with an arbitrary but fixed key. Released explicitly: a pooled connection would otherwise keep the lock.
    DbUp.Engine.DatabaseUpgradeResult result;
    using (var migrationLock = new NpgsqlConnection(connectionString))
    {
        migrationLock.Open();
        using (var take = new NpgsqlCommand("select pg_advisory_lock(5821)", migrationLock)) take.ExecuteNonQuery();
        try { result = upgrader.PerformUpgrade(); }
        finally
        {
            using var release = new NpgsqlCommand("select pg_advisory_unlock(5821)", migrationLock);
            release.ExecuteNonQuery();
        }
    }
    if (!result.Successful)
    {
        PrivateLogProvider.WriteFailure(Console.Error, PrivateLogProvider.MigrationFailed, result.Error);
        return 1;
    }

    var api = app.MapGroup("/api");

    api.MapGet("/health", async (NpgsqlDataSource db) =>
    {
        await using var conn = await db.OpenConnectionAsync();
        var postgis = await conn.ExecuteScalarAsync<string>("select postgis_version()");
        return Results.Ok(new { status = "ok", postgis });
    });

    api.MapGet("/locale", (HttpContext http) =>
    {
        // IP-based default languages must not be reused across visitors or networks.
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { isSwedishIp = SwedishIp.Contains(http.Connection.RemoteIpAddress) });
    });

    api.MapAntiforgery();
    api.MapAuth();
    api.MapMe();
    api.MapPrivacyPlaces();
    api.MapDataExport();

    api.MapActivities();
    api.MapSocial();
    api.MapPublic();
    api.MapEquipment();
    api.MapBestEfforts();
    api.MapJobs();
    api.MapGoals();
    api.MapGarmin();
    app.MapGarminPush();

    app.MapFallbackToFile("/privacy", "privacy.html", staticFiles);
    app.MapFallbackToFile("/terms", "terms.html", staticFiles);
    app.MapFallbackToFile("/features", "features.html", staticFiles);

    // The public pages are static; only the app's known paths get the React shell.
    foreach (var path in new[] { "/app", "/app/activities", "/app/activities/new", "/app/activities/{activityId:guid}", "/app/activities/{activityId:guid}/edit", "/app/stats", "/app/profile", "/app/settings/equipment", "/app/settings/followers", "/app/settings/privacy-places", "/app/runners/{runnerId:guid}", "/app/settings" })
        app.MapFallbackToFile(path, "app.html", staticFiles);

    await app.RunAsync();
    return 0;
}

// Visible to WebApplicationFactory in the tests
public partial class Program;
