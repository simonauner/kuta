using Dapper;
using Npgsql;

public sealed record StravaShoe(string Id, string Name, string? Brand);

public static class StravaEquipment
{
    public static async Task<(Dictionary<string, Guid> Ids, int Imported)> ImportAsync(
        NpgsqlConnection conn, Guid userId, IReadOnlyList<StravaShoe> shoes, CancellationToken ct)
    {
        var ids = new Dictionary<string, Guid>();
        var imported = 0;
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var shoe in shoes)
        {
            var id = await conn.QuerySingleOrDefaultAsync<Guid?>(
                """
                insert into equipment (user_id, name, brand, kind, strava_gear_id)
                values (@userId, @Name, @Brand, 'shoe', @Id)
                on conflict (user_id, strava_gear_id) where strava_gear_id is not null do nothing
                returning id
                """, new { userId, shoe.Id, shoe.Name, shoe.Brand }, tx);
            if (id is not null) imported++;
            // Keep the user's name, starting values and retirement on reimport.
            ids.Add(shoe.Id, id ?? await conn.QuerySingleAsync<Guid>(
                "select id from equipment where user_id=@userId and strava_gear_id=@Id", new { userId, shoe.Id }, tx));
        }
        await tx.CommitAsync(ct);
        return (ids, imported);
    }

    // Only fill in activities without equipment; the user's own choice on an activity must never be overwritten.
    public static async Task<bool> AttachMissingAsync(NpgsqlConnection conn, Guid userId, Guid equipmentId,
        string stravaId, byte[]? hash, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.QuerySingleOrDefaultAsync<Guid?>(
            """
            select id from activities
            where user_id=@userId and (strava_activity_id=@stravaId or raw_file_sha256=@hash)
            order by case when strava_activity_id=@stravaId then 0 else 1 end
            limit 1 for update
            """, new { userId, stravaId, hash }, tx);
        if (id is null || await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from activity_equipment where activity_id=@id and user_id=@userId)",
            new { id, userId }, tx)) return false;
        if (!await EquipmentEndpoints.OwnsAsync(conn, tx, userId, [equipmentId], allowRetired: true)) return false;
        await EquipmentEndpoints.AttachAsync(conn, tx, id.Value, userId, [equipmentId]);
        await tx.CommitAsync(ct);
        return true;
    }
}
