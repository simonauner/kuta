using Dapper;
using Npgsql;

public record ActivityCursor(DateTime StartedAt, Guid Id);
public record ActivityPage(IReadOnlyList<ActivityListItem> Items, ActivityCursor? NextCursor);
public record ActivityListItem(
    Guid Id, string Sport, string? Title, DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, string[] Tags, Guid[] EquipmentIds, bool ShowMonth, bool ShowWeek);

public static class ActivityList
{
    public static Task<bool> IsValidTimeZoneAsync(NpgsqlConnection conn, string timeZone, CancellationToken ct) =>
        conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from pg_timezone_names where name = @timeZone)",
            new { timeZone }, cancellationToken: ct));

    public static async Task<int[]> ReadYearsAsync(NpgsqlConnection conn, Guid userId, string timeZone, CancellationToken ct) =>
        (await conn.QueryAsync<int>(new CommandDefinition("""
            select distinct extract(year from started_at at time zone @timeZone)::int as year
            from activities where user_id = @userId order by year desc
            """, new { userId, timeZone }, cancellationToken: ct))).ToArray();

    public static async Task<ActivityPage> ReadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTimeOffset? beforeStartedAt = null, Guid? beforeId = null, CancellationToken ct = default, string? tag = null, int? year = null)
    {
        // Läs en extra rad för att avgöra om det finns en sida till. Markören kräver ingen kvarvarande rad.
        var cursorFilter = beforeStartedAt.HasValue ? "and (a.started_at, a.id) < (@beforeStartedAt, @beforeId)" : "";
        var yearFilter = year.HasValue ? """
            and a.started_at >= (make_date(@year, 1, 1)::timestamp at time zone @timeZone)
            and a.started_at < (make_date(@year + 1, 1, 1)::timestamp at time zone @timeZone)
            """ : "";
        await using var command = new NpgsqlCommand($"""
            with page as (
                select a.id, a.sport, a.title, a.started_at, a.duration_s, a.distance_m, a.avg_hr, a.tags
                from activities a
                where a.user_id = @userId and a.tags @> @tags::text[] {cursorFilter} {yearFilter}
                order by a.started_at desc, a.id desc
                limit 26
            )
            select a.id, a.sport, a.title, a.started_at, a.duration_s, a.distance_m, a.avg_hr, a.tags,
                array(select equipment_id from activity_equipment where activity_id = a.id order by equipment_id) as equipment_ids,
                exists (
                    select 1 from activities other
                    where other.user_id = @userId and other.id <> a.id and other.tags @> @tags::text[]
                      and other.started_at >= (period.year_start at time zone @timeZone)
                      and other.started_at < ((period.year_start + interval '1 year') at time zone @timeZone)
                ) as show_month,
                exists (
                    select 1 from activities other
                    where other.user_id = @userId and other.id <> a.id and other.tags @> @tags::text[]
                      and other.started_at >= (period.month_start at time zone @timeZone)
                      and other.started_at < ((period.month_start + interval '1 month') at time zone @timeZone)
                ) as show_week
            from page a
            cross join lateral (
                select date_trunc('year', a.started_at at time zone @timeZone) as year_start,
                       date_trunc('month', a.started_at at time zone @timeZone) as month_start
            ) period
            order by a.started_at desc, a.id desc
            """, conn);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("timeZone", timeZone);
        command.Parameters.AddWithValue("tags", tag is null ? Array.Empty<string>() : [tag]);
        if (year.HasValue) command.Parameters.AddWithValue("year", year.Value);
        if (beforeStartedAt.HasValue)
        {
            command.Parameters.AddWithValue("beforeStartedAt", beforeStartedAt.Value.UtcDateTime);
            command.Parameters.AddWithValue("beforeId", beforeId!.Value);
        }
        // Npgsql reports arrays as System.Array, which Dapper cannot match to typed record parameters.
        var rows = new List<ActivityListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new ActivityListItem(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTime(3), reader.GetInt32(4),
                reader.GetDouble(5), reader.IsDBNull(6) ? null : reader.GetInt16(6),
                reader.GetFieldValue<string[]>(7), reader.GetFieldValue<Guid[]>(8), reader.GetBoolean(9), reader.GetBoolean(10)));
        var items = rows.Take(25).ToList();
        var cursor = rows.Count > 25 ? new ActivityCursor(items[^1].StartedAt, items[^1].Id) : null;
        return new ActivityPage(items, cursor);
    }
}
