using System.Reflection;
using Dapper;
using DbUp;
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

// Platshållare tills auth finns: lista pass för en användare
api.MapGet("/activities", async (NpgsqlDataSource db, Guid userId) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var rows = await conn.QueryAsync(
        """
        select id, sport, title, started_at, duration_s, distance_m, avg_hr, shoe_id
        from activities
        where user_id = @userId
        order by started_at desc
        limit 50
        """,
        new { userId });
    return Results.Ok(rows);
});

app.Run();
return 0;
