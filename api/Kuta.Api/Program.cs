using System.Reflection;
using System.Security.Claims;
using Dapper;
using DbUp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url saknas");

// Inloggning sköts av Supabase Auth; API:t validerar dess JWT:er (nycklar hämtas via OIDC-discovery/JWKS)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = $"{supabaseUrl}/auth/v1";
        o.Audience = "authenticated";
        o.MapInboundClaims = false;
        o.RequireHttpsMetadata = !new Uri(supabaseUrl).IsLoopback;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

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
