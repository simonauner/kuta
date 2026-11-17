using System.Security.Claims;
using Dapper;
using Npgsql;

public sealed record Companion(Guid ActivityId, Guid UserId, string DisplayName, Guid? AvatarId, string Sport, string? Title);
public sealed record CompanionTrack(DateTime StartedAt, int[] T, double?[]? Lat, double?[]? Lon);

// "Ran together": other users' activities that were near this one at the same time. Shown only for activities the viewer
// may read on both sides, so it reveals nothing they could not already see, and never when either owner opted out
// (activities.hide_companions). Computed when read; nothing is stored.
public static class Companions
{
    // shortcut: initial values, not calibrated against real group runs. Change them here; nothing is stored.
    public const double NearM = 100;
    // Together when near for at least this long and for at least half of the time both were recording.
    public const int MinTogetherS = 600;
    public const double MinShare = 0.5;
    // Positions further apart in time than this are not compared (GPS gaps, pauses).
    const int MaxSampleGapS = 15;
    const int StepS = 10;
    const int MaxCandidates = 20;

    public static bool RanTogether(CompanionTrack a, CompanionTrack b)
    {
        if (a.Lat is null || a.Lon is null || b.Lat is null || b.Lon is null || a.T.Length == 0 || b.T.Length == 0)
            return false;
        // b's offset from a's start, so both use a's clock in seconds.
        var shift = (int)Math.Round((b.StartedAt - a.StartedAt).TotalSeconds);
        int compared = 0, near = 0, next = int.MinValue;
        for (var i = 0; i < a.T.Length; i++)
        {
            if (a.T[i] < next || a.Lat[i] is not { } lat || a.Lon[i] is not { } lon) continue;
            next = a.T[i] + StepS;
            var j = Array.BinarySearch(b.T, a.T[i] - shift);
            if (j < 0) j = Nearest(b.T, ~j, a.T[i] - shift);
            if (Math.Abs(b.T[j] + shift - a.T[i]) > MaxSampleGapS || b.Lat[j] is not { } otherLat || b.Lon[j] is not { } otherLon)
                continue;
            compared++;
            if (Distance(lat, lon, otherLat, otherLon) <= NearM) near++;
        }
        return near * StepS >= MinTogetherS && near >= compared * MinShare;
    }

    static int Nearest(int[] t, int insert, int value) =>
        insert == 0 ? 0 : insert == t.Length ? t.Length - 1
        : value - t[insert - 1] <= t[insert] - value ? insert - 1 : insert;

    // Haversine distance in metres.
    static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = double.DegreesToRadians(lat2 - lat1);
        var dLon = double.DegreesToRadians(lon2 - lon1);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
            Math.Cos(double.DegreesToRadians(lat1)) * Math.Cos(double.DegreesToRadians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(h));
    }

    sealed record Candidate(Companion Companion, CompanionTrack Track);

    // Npgsql's reader, since GPS arrays contain nulls that Dapper cannot map to double?[].
    static async Task<List<Candidate>> QueryAsync(NpgsqlConnection conn, string sql, object args, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, conn);
        foreach (var property in args.GetType().GetProperties())
            command.Parameters.AddWithValue(property.Name, property.GetValue(args)!);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Candidate>();
        while (await reader.ReadAsync(ct))
        {
            T? Value<T>(int i) => reader.IsDBNull(i) ? default : reader.GetFieldValue<T>(i);
            rows.Add(new Candidate(
                new Companion(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), Value<Guid?>(3), reader.GetString(4),
                    Value<string>(5)),
                new CompanionTrack(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc), Value<int[]>(7) ?? [],
                    Value<double?[]>(8), Value<double?[]>(9))));
        }
        return rows;
    }

    // Null when the viewer may not read the activity.
    public static async Task<IReadOnlyList<Companion>?> ReadAsync(NpgsqlConnection conn, Guid id, Guid userId, CancellationToken ct)
    {
        if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                $"select exists (select 1 from activities a where a.id = @id and {SocialEndpoints.CanRead})",
                new { id, userId }, cancellationToken: ct)))
            return null;
        var own = await QueryAsync(conn, """
            select a.id, a.user_id, '', null::uuid, a.sport, a.title, a.started_at, s.t, s.lat, s.lon
            from activities a join activity_streams s on s.activity_id = a.id
            where a.id = @id and not a.hide_companions and a.route is not null and s.lat is not null
            """, new { id }, ct);
        if (own is not [{ Track: var track }]) return [];
        // The route index finds activities that came near at all; the streams decide whether it was at the same time.
        var candidates = await QueryAsync(conn, $"""
            select a.id, a.user_id, u.display_name, u.avatar_id, a.sport, a.title, a.started_at, s.t, s.lat, s.lon
            from activities me
            join activities a on a.started_at < me.started_at + me.duration_s * interval '1 second'
                and a.started_at + a.duration_s * interval '1 second' > me.started_at
                and a.started_at > me.started_at - interval '2 days'
                and a.user_id <> me.user_id and not a.hide_companions and a.route is not null
                and st_dwithin(a.route, me.route, @near)
            join users u on u.id = a.user_id
            join activity_streams s on s.activity_id = a.id
            where me.id = @id and {SocialEndpoints.CanRead}
            order by a.started_at, a.id
            limit @max
            """, new { id, userId, near = NearM, max = MaxCandidates }, ct);
        return candidates.Where(c => RanTogether(track, c.Track)).Select(c => c.Companion).ToList();
    }

    public static void MapCompanions(this RouteGroupBuilder api) =>
        api.MapGet("/activities/{id:guid}/companions", async (Guid id, NpgsqlDataSource db, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return await ReadAsync(conn, id, user.GetUserId(), ct) is { } companions ? Results.Ok(companions) : Results.NotFound();
        }).RequireAuthorization();
}
