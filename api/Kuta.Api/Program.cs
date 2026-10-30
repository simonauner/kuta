using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Dapper;
using DbUp;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// GSS-kryptering (Kerberos) av: vi loggar in med lösenord, och utan libgssapi i imagen loggar Npgsql ett fel vid varje försök
var connectionString = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default saknas")) { GssEncryptionMode = GssEncryptionMode.Disable }
    .ConnectionString;

// Dapper: snake_case-kolumner -> PascalCase-properties
DefaultTypeMap.MatchNamesWithUnderscores = true;

// Npgsql med PostGIS/NetTopologySuite
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseNetTopologySuite();
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// Inloggning via vårt API mot Supabase Auth (BFF); sessionen ligger i en krypterad HttpOnly-cookie
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<SupabaseAuthClient>();
builder.Services.AddSingleton<SharedTokenRefresh>();
builder.Services.AddSingleton<RawFileStore>();

// Nycklarna sparas i Postgres och krypteras med ett certifikat utanför Development (DataProtection__Certificate)
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>, PostgresKeyManagementSetup>();
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("kuta");
var certificates = DataProtectionCertificates.Load(builder.Configuration, requireCurrent: !builder.Environment.IsDevelopment());
if (certificates.Current is { } currentCertificate) dataProtection.ProtectKeysWithCertificate(currentCertificate);
if (certificates.Previous is { } previousCertificate) dataProtection.UnprotectKeysWithAnyCertificate(previousCertificate);

// Bakom Flys proxy (ASPNETCORE_FORWARDEDHEADERS_ENABLED): klientens IP i ForwardedHeaders__ForwardedForHeaderName
// (Fly-Client-IP) för rate limiten, och X-Forwarded-Proto så att appen ser https.
builder.Services.Configure<ForwardedHeadersOptions>(builder.Configuration.GetSection("ForwardedHeaders"));
builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

// __Host- kräver Secure, som inte går över http lokalt
var cookiePrefix = builder.Environment.IsDevelopment() ? "" : "__Host-";
var cookieSecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = cookiePrefix + "kuta.session";
        o.Cookie.HttpOnly = true;
        // Lax så att länken i bekräftelsemejlet öppnar appen inloggad; skrivningar skyddas av antiforgery-token
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = cookieSecurePolicy;
        o.ExpireTimeSpan = SupabaseCookieSession.IdleTimeout;
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = SupabaseCookieSession.ValidatePrincipalAsync;
        // API: svara 401/403 i stället för att redirecta till en inloggningssida
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

// Bromsar lösenordsgissning och massregistrering: 10 auth-anrop per 5 minuter och IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
});

var app = builder.Build();

// API:t svarar bara med JSON och redirects: inget får köras eller laddas från svaren. Webbens CSP ligger i
// index.html (web/vite.config.ts); headern lägger till det en meta-tagg inte kan, frame-ancestors.
app.Use((context, next) =>
{
    var api = context.Request.Path.StartsWithSegments("/api");
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = api ? "default-src 'none'; frame-ancestors 'none'" : "frame-ancestors 'none'";
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = api ? "no-referrer" : "strict-origin-when-cross-origin";
    return next(context);
});
if (!app.Environment.IsDevelopment()) app.UseHsts();

// En adress för appen: kuta.fly.dev och www skickas vidare till CanonicalHost (kutaapp.com i fly.toml), så att
// cookies, länkar och bekräftelsemejl alltid gäller samma origin. Flys hälsokontroll går mot maskinens IP och berörs inte.
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

// I drift ligger den byggda webben i wwwroot (se Dockerfile), så att webb och API delar origin.
// Filnamnen under /assets innehåller en hash och kan cachas för alltid; resten (index.html, sw.js) ska inte cachas.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
        ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);
// Uttryckligen i den här ordningen: annars lägger WebApplication routing och auth först, så att 401/403 saknar
// headers och fallback-routen tar sw.js m.fl. innan de statiska filerna hittas.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Kör SQL-migreringar (inbäddade i Migrations/) vid start
var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

// Bara en process i taget migrerar (parallella teststarter, flera maskiner): ett sessionslås i Postgres runt DbUp,
// med ett godtyckligt men fast nyckeltal. Släpps uttryckligen: en poolad anslutning behåller annars låset.
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
    Console.Error.WriteLine(result.Error);
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
    // IP-baserade standardspråk får inte återanvändas mellan besökare eller nätverk.
    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { isSwedishIp = SwedishIp.Contains(http.Connection.RemoteIpAddress) });
});

api.MapAntiforgery();
api.MapAuth();
api.MapMe();

api.MapActivities();
api.MapBestEfforts();

// Startsidan är statisk; bara appens kända sökvägar får React-skalet.
foreach (var path in new[] { "/app", "/app/activities", "/app/activities/{activityId:guid}", "/app/stats", "/app/profile" })
    app.MapFallbackToFile(path, "app.html", staticFiles);

app.Run();
return 0;

// Synlig för WebApplicationFactory i testerna
public partial class Program;
