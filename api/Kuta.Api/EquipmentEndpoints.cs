using System.Security.Claims;
using Dapper;
using Npgsql;

public record EquipmentRequest(string? Name, string? Brand, string? Kind, double InitialDistanceM = 0, long InitialDurationS = 0);
public record EquipmentSummary(Guid Id, string Name, string? Brand, string Kind, bool Retired,
    double InitialDistanceM, long InitialDurationS, double DistanceM, long DurationS);
public record ActivityEquipmentRequest(Guid[] EquipmentIds);

public static class EquipmentEndpoints
{
    public static readonly string[] Kinds = ["shoe"];
    public static readonly ApiError InvalidEquipment = new("Välj giltig utrustning som tillhör dig.", "invalidEquipment");

    public static ApiError? Validate(EquipmentRequest req) =>
        string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 100 || req.Brand?.Trim().Length > 100
        || req.Kind is null || !Kinds.Contains(req.Kind)
        || !double.IsFinite(req.InitialDistanceM) || req.InitialDistanceM is < 0 or > 1_000_000_000
        || req.InitialDurationS is < 0 or > 100L * 365 * 24 * 3600
            ? new ApiError("Kontrollera namn, typ och tidigare användning.", "invalidEquipmentInput") : null;

    public static void MapEquipment(this RouteGroupBuilder api)
    {
        var equipment = api.MapGroup("/equipment").RequireAuthorization();
        equipment.MapGet("/kinds", () => Results.Ok(Kinds));
        equipment.MapGet("", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            return Results.Ok(await LoadAsync(conn, user.GetUserId()));
        });
        equipment.MapPost("", async (EquipmentRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (Validate(req) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync();
            var userId = user.GetUserId();
            if (await MeEndpoints.LoadProfileAsync(conn, userId) is null)
                return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
            var id = await conn.ExecuteScalarAsync<Guid>(
                """
                insert into equipment (user_id, name, brand, kind, initial_distance_m, initial_duration_s)
                values (@userId, @name, @brand, @Kind, @InitialDistanceM, @InitialDurationS) returning id
                """, new { userId, name = req.Name!.Trim(), brand = ActivityInput.NormalizeTitle(req.Brand),
                    req.Kind, req.InitialDistanceM, req.InitialDurationS });
            return Results.Created($"/api/equipment/{id}", new EquipmentSummary(id, req.Name!.Trim(),
                ActivityInput.NormalizeTitle(req.Brand), req.Kind!, false, req.InitialDistanceM, req.InitialDurationS,
                req.InitialDistanceM, req.InitialDurationS));
        });

        api.MapPut("/activities/{id:guid}/equipment", async (Guid id, ActivityEquipmentRequest req,
            NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (req.EquipmentIds is null) return Results.BadRequest(InvalidEquipment);
            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var userId = user.GetUserId();
            var owned = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "select id from activities where id = @id and user_id = @userId for update", new { id, userId }, tx);
            if (owned is null) return Results.NotFound();
            if (!await OwnsAsync(conn, tx, userId, req.EquipmentIds)) return Results.BadRequest(InvalidEquipment);
            await conn.ExecuteAsync("delete from activity_equipment where activity_id = @id and user_id = @userId",
                new { id, userId }, tx);
            await AttachAsync(conn, tx, id, userId, req.EquipmentIds);
            await tx.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    // Sum directly from activities: changing a link or deleting an activity updates usage without counter drift.
    public static Task<IEnumerable<EquipmentSummary>> LoadAsync(NpgsqlConnection conn, Guid userId) =>
        conn.QueryAsync<EquipmentSummary>(
            """
            select e.id, e.name, e.brand, e.kind, e.retired, e.initial_distance_m, e.initial_duration_s,
                   e.initial_distance_m + coalesce(sum(a.distance_m), 0) as distance_m,
                   e.initial_duration_s + coalesce(sum(a.duration_s), 0)::bigint as duration_s
            from equipment e
            left join activity_equipment ae on ae.equipment_id = e.id and ae.user_id = e.user_id
            left join activities a on a.id = ae.activity_id and a.user_id = e.user_id
            where e.user_id = @userId
            group by e.id
            order by e.created_at, e.id
            """, new { userId });

    public static async Task<bool> OwnsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid userId, Guid[] equipmentIds)
    {
        var ids = equipmentIds.Distinct().ToArray();
        var owned = await conn.QueryAsync<Guid>(
            "select id from equipment where user_id = @userId and id = any(@ids) order by id for key share",
            new { userId, ids }, tx);
        return owned.Count() == ids.Length;
    }

    public static Task AttachAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid activityId, Guid userId, Guid[] equipmentIds) =>
        conn.ExecuteAsync(
            """
            insert into activity_equipment (activity_id, equipment_id, user_id)
            select @activityId, id, @userId from unnest(@ids::uuid[]) as id
            """, new { activityId, userId, ids = equipmentIds.Distinct().ToArray() }, tx);
}
