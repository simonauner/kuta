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

// Inloggning via vårt API mot Supabase Auth (BFF, som i Kredovis); sessionen ligger i en krypterad HttpOnly-cookie
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<SupabaseAuthClient>();
builder.Services.AddSingleton<SharedTokenRefresh>();

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

var result = upgrader.PerformUpgrade();
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

api.MapAntiforgery();
api.MapAuth();
api.MapMe();

// Inloggad användares senaste pass
api.MapGet("/activities", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var rows = await conn.QueryAsync<ActivitySummary>(
        """
        select id, sport, title, started_at, duration_s, distance_m, avg_hr, shoe_id
        from activities
        where user_id = @userId
        order by started_at desc
        limit 50
        """,
        new { userId = user.GetUserId() });
    return Results.Ok(rows);
}).RequireAuthorization();

// Appens egna sökvägar (/pass, /profil …) ger index.html; okända /api-sökvägar ska ge 404, inte appen.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html", staticFiles);

app.Run();
return 0;

// Synlig för WebApplicationFactory i testerna
public partial class Program;

// Typade records, inte dynamic: Dapper-rader serialiseras som dictionary med snake_case-nycklar.
public record ActivitySummary(
    Guid Id, string Sport, string? Title, DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, Guid? ShoeId);
