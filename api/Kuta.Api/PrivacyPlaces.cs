using System.Security.Claims;
using Dapper;
using Npgsql;

public record PrivacyPlace(Guid Id, string Name, double Latitude, double Longitude, int RadiusM);
public record PrivacyPlaceRequest(string? Name, double? Latitude, double? Longitude, int RadiusM = 200);

public static class PrivacyPlaces
{
    public static ApiError? Validate(PrivacyPlaceRequest req) =>
        string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 100
        || req.Latitude is not (>= -90 and <= 90)
        || req.Longitude is not (>= -180 and <= 180)
        || req.RadiusM is < 50 or > 5000
            ? new ApiError("Ange ett namn, en giltig kartpunkt och en radie mellan 50 och 5 000 meter.", "invalidPrivacyPlace") : null;

    public static void MapPrivacyPlaces(this RouteGroupBuilder api)
    {
        var places = api.MapGroup("/me/privacy-places").RequireAuthorization();
        places.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            await using var conn = await db.OpenConnectionAsync(ct);
            return Results.Ok(await LoadAsync(conn, user.GetUserId(), ct));
        });
        places.MapPost("", async (PrivacyPlaceRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (Validate(req) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(ct);
            if (await MeEndpoints.LoadProfileAsync(conn, user.GetUserId()) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            var place = await CreateAsync(conn, user.GetUserId(), req, ct);
            return Results.Created($"/api/me/privacy-places/{place.Id}", place);
        });
        places.MapPut("/{id:guid}", async (Guid id, PrivacyPlaceRequest req, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (Validate(req) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync(ct);
            return await UpdateAsync(conn, user.GetUserId(), id, req, ct) ? Results.NoContent() : Results.NotFound();
        });
        places.MapDelete("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await DeleteAsync(conn, user.GetUserId(), id, ct) ? Results.NoContent() : Results.NotFound();
        });
    }

    public static async Task<PrivacyPlace[]> LoadAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct,
        NpgsqlTransaction? tx = null) =>
        (await conn.QueryAsync<PrivacyPlace>(new CommandDefinition("""
            select id, name, latitude, longitude, radius_m from privacy_places
            where user_id = @userId order by name, id
            """, new { userId }, tx, cancellationToken: ct))).ToArray();

    public static Task<PrivacyPlace> CreateAsync(NpgsqlConnection conn, Guid userId, PrivacyPlaceRequest req, CancellationToken ct) =>
        conn.QuerySingleAsync<PrivacyPlace>(new CommandDefinition("""
            insert into privacy_places (user_id, name, latitude, longitude, radius_m)
            values (@userId, @name, @Latitude, @Longitude, @RadiusM)
            returning id, name, latitude, longitude, radius_m
            """, new { userId, name = req.Name!.Trim(), req.Latitude, req.Longitude, req.RadiusM }, cancellationToken: ct));

    public static async Task<bool> UpdateAsync(NpgsqlConnection conn, Guid userId, Guid id, PrivacyPlaceRequest req, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("""
            update privacy_places set name = @name, latitude = @Latitude, longitude = @Longitude, radius_m = @RadiusM
            where id = @id and user_id = @userId
            """, new { id, userId, name = req.Name!.Trim(), req.Latitude, req.Longitude, req.RadiusM }, cancellationToken: ct)) > 0;

    public static async Task<bool> DeleteAsync(NpgsqlConnection conn, Guid userId, Guid id, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition("delete from privacy_places where id = @id and user_id = @userId",
            new { id, userId }, cancellationToken: ct)) > 0;
}
