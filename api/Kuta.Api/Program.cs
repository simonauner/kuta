using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Dapper;
using DbUp;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default saknas");

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

// shortcut: nycklarna ligger okrypterade i Postgres; skydda dem med certifikat (som Kredovis) före produktion.
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>, PostgresKeyManagementSetup>();
builder.Services.AddDataProtection().SetApplicationName("kuta");

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

app.Run();
return 0;

// Synlig för WebApplicationFactory i testerna
public partial class Program;

// Typade records, inte dynamic: Dapper-rader serialiseras som dictionary med snake_case-nycklar.
public record ActivitySummary(
    Guid Id, string Sport, string? Title, DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, Guid? ShoeId);
