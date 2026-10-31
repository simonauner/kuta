using System.Security.Claims;
using Dapper;
using Npgsql;

public record ActivityPart(int PartIndex, string Sport, int StartS, int EndS, double DistanceM, bool UseEquipment);
public record ActivityPartInput(string Sport, int StartS, int EndS, bool UseEquipment);
public record MultisportRequest(string Kind, ActivityPartInput[] Parts);

public static class Multisport
{
    public static readonly string[] Kinds = ["triathlon", "swimrun", "multisport"];
    public static readonly string[] Sports = ["running", "cycling", "swimming", "walking", "hiking", "transition", "other"];
    public static readonly ApiError InvalidParts = new("Kontrollera sporter och gränser. Delarna måste täcka hela inspelningen utan luckor eller överlapp.", "invalidActivityParts");

    public static bool CanSplit(int[] t, float?[]? dist) => t.Length >= 2 && dist?.Length == t.Length
        && t[0] >= 0 && t[^1] > t[0]
        && t.Select((v, i) => i == 0 || v > t[i - 1]).All(v => v)
        && dist.All(d => d is { } v && float.IsFinite(v) && v >= 0)
        && dist.Select((v, i) => i == 0 || v >= dist[i - 1]).All(v => v);

    public static ActivityPart[]? Build(MultisportRequest req, int[] t, float?[]? dist)
    {
        if (!Kinds.Contains(req.Kind) || req.Parts is not { Length: >= 2 and <= 200 }
            || !CanSplit(t, dist)) return null;
        var result = new List<ActivityPart>();
        var end = t[0];
        foreach (var p in req.Parts)
        {
            if (p is null || !Sports.Contains(p.Sport) || p.StartS != end || p.EndS <= p.StartS || p.EndS > t[^1]) return null;
            result.Add(new(result.Count, p.Sport, p.StartS, p.EndS,
                DistanceAt(t, dist!, p.EndS) - DistanceAt(t, dist!, p.StartS), p.UseEquipment));
            end = p.EndS;
        }
        var sports = result.Select(p => p.Sport).Where(s => s != "transition").ToArray();
        if (req.Kind == "triathlon" && !sports.SequenceEqual(new[] { "swimming", "cycling", "running" })) return null;
        if (req.Kind == "swimrun" && sports.Any(s => s is not ("swimming" or "running"))) return null;
        return end == t[^1] && sports.Distinct().Count() >= 2 ? result.ToArray() : null;
    }

    public static double DistanceAt(int[] t, float?[] dist, int seconds)
    {
        var i = Array.BinarySearch(t, seconds);
        if (i >= 0) return dist[i]!.Value;
        i = ~i;
        return dist[i - 1]!.Value + (dist[i]!.Value - dist[i - 1]!.Value)
            * (seconds - t[i - 1]) / (double)(t[i] - t[i - 1]);
    }

    // Interpolate only the cut endpoints; corrupt/missing samples inside the leg still break record windows.
    public static (int[] T, float?[]? Dist) Slice(int[] t, float?[]? dist, ActivityPart p)
    {
        if (dist?.Length != t.Length || t.Length < 2 || t.Select((v, i) => i > 0 && v <= t[i - 1]).Any(v => v)) return ([], null);
        var points = t.Select((v, i) => (T: v, D: dist[i]))
            .Where(v => v.T >= p.StartS && v.T <= p.EndS).ToList();
        foreach (var boundary in new[] { p.StartS, p.EndS })
        {
            var i = Array.BinarySearch(t, boundary);
            if (i >= 0) continue;
            i = ~i;
            if (i > 0 && i < t.Length && t[i] > t[i - 1]
                && dist[i - 1] is { } a && dist[i] is { } b && float.IsFinite(a) && float.IsFinite(b) && b >= a)
                points.Add((boundary, a + (b - a) * (boundary - t[i - 1]) / (t[i] - t[i - 1])));
        }
        points.Sort((a, b) => a.T.CompareTo(b.T));
        return (points.Select(v => v.T).ToArray(), points.Select(v => v.D).ToArray());
    }

    public static async Task<ActivityPart[]> LoadAsync(NpgsqlConnection conn, Guid id, NpgsqlTransaction? tx = null) =>
        (await conn.QueryAsync<ActivityPart>("""
            select p.part_index, p.sport, p.start_s, p.end_s, p.distance_m, p.use_equipment
            from activity_parts p join activities a on a.id = p.activity_id
            where a.id = @id and p.original = not a.parts_edited order by p.part_index
            """, new { id }, tx)).ToArray();

    public static Task SavePartsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, ActivityPart[] parts, bool original) =>
        conn.ExecuteAsync("""
            insert into activity_parts (activity_id, original, part_index, sport, start_s, end_s, distance_m, use_equipment)
            values (@id, @original, @PartIndex, @Sport, @StartS, @EndS, @DistanceM, @UseEquipment)
            """, parts.Select(p => new { id, original, p.PartIndex, p.Sport, p.StartS, p.EndS, p.DistanceM, p.UseEquipment }), tx);

    public static async Task<(int[] T, float?[]? Dist)> ReadStreamAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id)
    {
        await using var command = new NpgsqlCommand("select t, dist from activity_streams where activity_id = @id", conn, tx);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (reader.GetFieldValue<int[]>(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<float?[]>(1)) : ([], null);
    }

    public static async Task<int> UpdateAsync(NpgsqlConnection conn, Guid userId, Guid id, MultisportRequest? req)
    {
        await using var tx = await conn.BeginTransactionAsync();
        var activity = await conn.QuerySingleOrDefaultAsync<PartOwner>(
            "select sport, started_at from activities where id = @id and user_id = @userId for update", new { id, userId }, tx);
        if (activity is null) return 404;
        var (t, dist) = await ReadStreamAsync(conn, tx, id);
        ActivityPart[] parts = [];
        if (req is not null)
        {
            var built = Build(req, t, dist);
            if (built is null) return 400;
            parts = built;
        }
        await conn.ExecuteAsync("delete from activity_parts where activity_id = @id and not original", new { id }, tx);
        if (req is not null)
        {
            await SavePartsAsync(conn, tx, id, parts, false);
            await conn.ExecuteAsync("""
                update activities set original_sport = coalesce(original_sport, sport), sport = @Kind, parts_edited = true
                where id = @id and user_id = @userId
                """, new { id, userId, req.Kind }, tx);
        }
        else
        {
            await conn.ExecuteAsync("""
                update activities set sport = coalesce(original_sport, sport), parts_edited = false
                where id = @id and user_id = @userId
                """, new { id, userId }, tx);
            parts = await LoadAsync(conn, id, tx);
        }
        var sport = req?.Kind ?? await conn.QuerySingleAsync<string>("select sport from activities where id = @id", new { id }, tx);
        await BestEfforts.ReplaceAsync(conn, tx, id, userId, sport, activity.StartedAt, t, dist, parts);
        await tx.CommitAsync();
        return 204;
    }

    public static void MapMultisport(this RouteGroupBuilder activities)
    {
        activities.MapPut("/{id:guid}/parts", async (Guid id, MultisportRequest req, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var status = await UpdateAsync(conn, user.GetUserId(), id, req);
            return status == 400 ? Results.BadRequest(InvalidParts) : Results.StatusCode(status);
        });
        activities.MapDelete("/{id:guid}/parts", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            return Results.StatusCode(await UpdateAsync(conn, user.GetUserId(), id, null));
        });
    }
    private record PartOwner(string Sport, DateTime StartedAt);
}
