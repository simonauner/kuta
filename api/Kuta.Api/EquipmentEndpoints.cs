using System.Security.Claims;
using Dapper;
using Npgsql;

public record EquipmentRequest(string? Name, string? Brand, string? Kind, double InitialDistanceM = 0, long InitialDurationS = 0);
public record EquipmentSummary(Guid Id, string Name, string? Brand, string Kind, bool Retired,
    double InitialDistanceM, long InitialDurationS, double DistanceM, long DurationS);
public record LastUsedShoe(Guid? Id);
public sealed class InvalidEquipmentException : Exception;

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
        equipment.MapGet("/last-used-shoe", async (NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var id = await conn.QuerySingleOrDefaultAsync<Guid?>(
                """
                select e.id from equipment e
                join activity_equipment ae on ae.equipment_id = e.id and ae.user_id = e.user_id
                join activities a on a.id = ae.activity_id and a.user_id = ae.user_id
                where e.user_id = @userId and e.kind = 'shoe' and not e.retired
                order by a.started_at desc, a.created_at desc, a.id desc, e.id limit 1
                """, new { userId = user.GetUserId() });
            return Results.Ok(new LastUsedShoe(id));
        });
        equipment.MapMethods("/{id:guid}/retire", ["PUT", "DELETE"], async (Guid id, HttpRequest request, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                "update equipment set retired = @retired where id = @id and user_id = @userId",
                new { id, userId = user.GetUserId(), retired = HttpMethods.IsPut(request.Method) });
            return updated == 0 ? Results.NotFound() : Results.NoContent();
        });
        equipment.MapDelete("/{id:guid}", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            // The links go away via FK cascade; the activities are kept.
            var deleted = await conn.ExecuteAsync(
                "delete from equipment where id = @id and user_id = @userId", new { id, userId = user.GetUserId() });
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
        equipment.MapPut("/{id:guid}", async (Guid id, EquipmentRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            if (Validate(req) is { } invalid) return Results.BadRequest(invalid);
            await using var conn = await db.OpenConnectionAsync();
            var updated = await conn.ExecuteAsync(
                """
                update equipment set name = @name, brand = @brand, kind = @Kind,
                    initial_distance_m = @InitialDistanceM, initial_duration_s = @InitialDurationS
                where id = @id and user_id = @userId
                """, new { id, userId = user.GetUserId(), name = req.Name!.Trim(),
                    brand = ActivityInput.NormalizeTitle(req.Brand), req.Kind, req.InitialDistanceM, req.InitialDurationS });
            return updated == 0 ? Results.NotFound() : Results.NoContent();
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


    }

    // Sum directly from activities: changing a link or deleting an activity updates usage without counter drift.
    public static Task<IEnumerable<EquipmentSummary>> LoadAsync(NpgsqlConnection conn, Guid userId) =>
        conn.QueryAsync<EquipmentSummary>(
            """
            select e.id, e.name, e.brand, e.kind, e.retired, e.initial_distance_m, e.initial_duration_s,
                   e.initial_distance_m + coalesce(sum(coalesce(usage.distance_m, a.distance_m)), 0) as distance_m,
                   e.initial_duration_s + coalesce(sum(coalesce(usage.duration_s, a.duration_s)), 0)::bigint as duration_s
            from equipment e
            left join activity_equipment ae on ae.equipment_id = e.id and ae.user_id = e.user_id
            left join activities a on a.id = ae.activity_id and a.user_id = e.user_id
            left join lateral (
                select coalesce(sum(p.distance_m) filter (where p.use_equipment), 0) as distance_m,
                       coalesce(sum(p.end_s - p.start_s) filter (where p.use_equipment), 0) as duration_s
                from activity_parts p
                where p.activity_id = a.id and p.original = not a.parts_edited
                having count(*) > 0
            ) usage on true
            where e.user_id = @userId
            group by e.id
            order by e.retired, e.created_at, e.id
            """, new { userId });

    public static async Task<bool> OwnsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid userId, Guid[] equipmentIds, bool allowRetired = false)
    {
        var ids = equipmentIds.Distinct().ToArray();
        var owned = await conn.QueryAsync<Guid>(
            "select id from equipment where user_id = @userId and id = any(@ids) and (@allowRetired or not retired) order by id for key share",
            new { userId, ids, allowRetired }, tx);
        return owned.Count() == ids.Length;
    }

    public static Task AttachAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid activityId, Guid userId, Guid[] equipmentIds) =>
        conn.ExecuteAsync(
            """
            insert into activity_equipment (activity_id, equipment_id, user_id)
            select @activityId, id, @userId from unnest(@ids::uuid[]) as id
            """, new { activityId, userId, ids = equipmentIds.Distinct().ToArray() }, tx);
}
