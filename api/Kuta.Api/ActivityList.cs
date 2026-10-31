using Dapper;
using Npgsql;

public record ActivityCursor(DateTime StartedAt, Guid Id);
public record ActivityPage(IReadOnlyList<ActivityListItem> Items, ActivityCursor? NextCursor);
public record ActivityListItem(
    Guid Id, string Sport, string? Title, DateTime StartedAt,
    int DurationS, double DistanceM, short? AvgHr, Guid? ShoeId, bool ShowMonth, bool ShowWeek);

public static class ActivityList
{
    public static async Task<ActivityPage> ReadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTimeOffset? beforeStartedAt = null, Guid? beforeId = null, CancellationToken ct = default)
    {
        // Läs en extra rad för att avgöra om det finns en sida till. Markören kräver ingen kvarvarande rad.
        var cursorFilter = beforeStartedAt.HasValue ? "and (a.started_at, a.id) < (@beforeStartedAt, @beforeId)" : "";
        var rows = (await conn.QueryAsync<ActivityListItem>(new CommandDefinition($"""
            with page as (
                select a.id, a.sport, a.title, a.started_at, a.duration_s, a.distance_m, a.avg_hr, a.shoe_id
                from activities a
                where a.user_id = @userId {cursorFilter}
                order by a.started_at desc, a.id desc
                limit 26
            )
            select a.id, a.sport, a.title, a.started_at, a.duration_s, a.distance_m, a.avg_hr, a.shoe_id,
                exists (
                    select 1 from activities other
                    where other.user_id = @userId and other.id <> a.id
                      and other.started_at >= (period.year_start at time zone @timeZone)
                      and other.started_at < ((period.year_start + interval '1 year') at time zone @timeZone)
                ) as show_month,
                exists (
                    select 1 from activities other
                    where other.user_id = @userId and other.id <> a.id
                      and other.started_at >= (period.month_start at time zone @timeZone)
                      and other.started_at < ((period.month_start + interval '1 month') at time zone @timeZone)
                ) as show_week
            from page a
            cross join lateral (
                select date_trunc('year', a.started_at at time zone @timeZone) as year_start,
                       date_trunc('month', a.started_at at time zone @timeZone) as month_start
            ) period
            order by a.started_at desc, a.id desc
            """, new { userId, timeZone, beforeStartedAt = beforeStartedAt?.UtcDateTime, beforeId },
            cancellationToken: ct))).ToList();
        var items = rows.Take(25).ToList();
        var cursor = rows.Count > 25 ? new ActivityCursor(items[^1].StartedAt, items[^1].Id) : null;
        return new ActivityPage(items, cursor);
    }
}
