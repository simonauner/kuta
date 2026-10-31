using System.Security.Claims;
using Dapper;
using Npgsql;

public record Shoe(Guid Id, string Name, string? Brand, bool Retired);
public record ShoeList(IEnumerable<Shoe> Shoes, Guid? LastUsedShoeId);
public record CreateShoeRequest(string? Name, string? Brand)
{
    public ApiError? Validate() =>
        Name?.Trim() is not { Length: > 0 and <= 100 } || Brand?.Trim().Length > 100
            ? new ApiError("Ange skons namn och högst 100 tecken per fält.", "invalidShoe") : null;
}

public static class ShoeEndpoints
{
    public static readonly ApiError UnavailableShoe = new("Välj en av dina aktiva skor eller ingen sko.", "unavailableShoe");

    public static void MapShoes(this RouteGroupBuilder api)
    {
        var shoes = api.MapGroup("/shoes").RequireAuthorization();
        shoes.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            var rows = await conn.QueryAsync<Shoe>(
                "select id, name, brand, retired from shoes where user_id = @userId order by retired, created_at, id",
                new { userId });
            var lastUsedShoeId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                """
                select a.shoe_id from activities a join shoes s on s.id = a.shoe_id
                where a.user_id = @userId and s.user_id = @userId and not s.retired
                order by a.started_at desc, a.created_at desc, a.id desc limit 1
                """,
                new { userId });
            return Results.Ok(new ShoeList(rows, lastUsedShoeId));
        });

        shoes.MapPost("", async (CreateShoeRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (req.Validate() is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            if (await MeEndpoints.LoadProfileAsync(conn, userId) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            var shoe = await conn.QuerySingleAsync<Shoe>(
                """
                insert into shoes (user_id, name, brand) values (@userId, @name, @brand)
                returning id, name, brand, retired
                """, new { userId, name = req.Name!.Trim(), brand = string.IsNullOrWhiteSpace(req.Brand) ? null : req.Brand.Trim() });
            return Results.Created($"/api/shoes/{shoe.Id}", shoe);
        });

        shoes.MapPut("/{id:guid}/retire", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                "update shoes set retired = true where id = @id and user_id = @userId", new { id, userId = user.GetUserId() });
            return updated == 0 ? Results.NotFound() : Results.NoContent();
        });

        shoes.MapDelete("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            // FK:n sätter shoe_id till null på gamla pass; själva passen behålls.
            var deleted = await conn.ExecuteAsync(
                "delete from shoes where id = @id and user_id = @userId", new { id, userId = user.GetUserId() });
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    public static async Task<bool> CanUseAsync(NpgsqlConnection conn, Guid userId, Guid? shoeId) =>
        shoeId is null || await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from shoes where id = @shoeId and user_id = @userId and not retired)",
            new { shoeId, userId });
}
