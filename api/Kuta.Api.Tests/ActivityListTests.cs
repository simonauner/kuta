using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityListTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Cursor_pages_cover_tied_dates_and_survive_deleting_the_cursor_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = await Runner(conn);
        var idPrefix = userId.ToString()[..24];
        try
        {
            var empty = await ActivityList.ReadAsync(conn, userId, "UTC", ct: ct);
            Assert.Empty(empty.Items);
            Assert.Null(empty.NextCursor);
            await conn.ExecuteAsync("""
                insert into activities (id, user_id, source, started_at, duration_s, distance_m)
                select (@idPrefix || lpad(i::text, 12, '0'))::uuid,
                       @userId, 'manual', '2026-10-30T12:00:00Z', 1800, 5000
                from generate_series(1, 25) i
                """, new { userId, idPrefix });
            var exact = await ActivityList.ReadAsync(conn, userId, "UTC", ct: ct);
            Assert.Equal(25, exact.Items.Count);
            Assert.Null(exact.NextCursor);
            await conn.ExecuteAsync("""
                insert into activities (id, user_id, source, started_at, duration_s, distance_m)
                select (@idPrefix || lpad(i::text, 12, '0'))::uuid,
                       @userId, 'manual', '2026-10-30T12:00:00Z', 1800, 5000
                from generate_series(26, 51) i
                """, new { userId, idPrefix });
            var first = await ActivityList.ReadAsync(conn, userId, "UTC", ct: ct);
            Assert.Equal(25, first.Items.Count);
            var cursor = Assert.IsType<ActivityCursor>(first.NextCursor);
            await conn.ExecuteAsync("delete from activities where id = @id", new { id = cursor.Id });
            // Ett nytt pass framför markören får inte flytta sidgränsen.
            await Add(conn, userId, "2026-11-01T12:00:00Z");
            var second = await ActivityList.ReadAsync(conn, userId, "UTC", cursor.StartedAt, cursor.Id, ct);
            Assert.Equal(25, second.Items.Count);
            var next = Assert.IsType<ActivityCursor>(second.NextCursor);
            var last = await ActivityList.ReadAsync(conn, userId, "UTC", next.StartedAt, next.Id, ct);
            Assert.Single(last.Items);
            Assert.Null(last.NextCursor);
            var ids = first.Items.Concat(second.Items).Concat(last.Items).Select(a => a.Id).ToList();
            Assert.Equal(51, ids.Distinct().Count());
            Assert.Equal(ids.OrderByDescending(id => id.ToString(), StringComparer.Ordinal), ids);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }
    }

    [Fact]
    public async Task Heading_flags_use_all_owned_activities_and_local_month_and_year_boundaries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = await Runner(conn);
        var otherId = await Runner(conn);
        try
        {
            var lone = await Add(conn, userId, "2026-12-31T23:30:00Z"); // 2027 i Stockholm
            await Add(conn, otherId, "2027-01-02T12:00:00Z");
            var local = await ActivityList.ReadAsync(conn, userId, "Europe/Stockholm", ct: ct);
            var single = Assert.Single(local.Items);
            Assert.False(single.ShowMonth);
            Assert.False(single.ShowWeek);
            await Add(conn, userId, "2026-12-31T22:30:00Z"); // Fortfarande 2026
            var differentYears = await ActivityList.ReadAsync(conn, userId, "Europe/Stockholm", ct: ct);
            Assert.All(differentYears.Items, a => { Assert.False(a.ShowMonth); Assert.False(a.ShowWeek); });
            var utc = await ActivityList.ReadAsync(conn, userId, "UTC", ct: ct);
            Assert.All(utc.Items, a => { Assert.True(a.ShowMonth); Assert.True(a.ShowWeek); });
            await Add(conn, userId, "2027-01-31T23:30:00Z"); // Februari i Stockholm
            var months = await ActivityList.ReadAsync(conn, userId, "Europe/Stockholm", ct: ct);
            var january = months.Items.Single(a => a.Id == lone);
            Assert.True(january.ShowMonth);
            Assert.False(january.ShowWeek);
            // Fyll sidan så att januaripasset ligger ensamt sist; det andra ligger på nästa sida.
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m)
                select @userId, 'manual', '2027-01-20T12:00:00Z', 1800, 5000 from generate_series(1, 23)
                """, new { userId });
            await Add(conn, userId, "2026-12-31T23:15:00Z");
            var page = await ActivityList.ReadAsync(conn, userId, "Europe/Stockholm", ct: ct);
            Assert.Equal(lone, page.Items[^1].Id);
            Assert.True(page.Items[^1].ShowWeek);
            Assert.NotNull(page.NextCursor);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { userId, otherId } }); }
    }

    [Fact]
    public async Task Tag_filter_applies_before_pagination_and_heading_counts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var userId = await Runner(conn);
        try
        {
            await conn.ExecuteAsync("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, tags)
                select @userId, 'manual', '2026-10-30T12:00:00Z', 1800, 5000, array['race']
                from generate_series(1, 26);
                insert into activities (user_id, source, started_at, duration_s, distance_m, tags)
                values (@userId, 'manual', '2027-01-01T12:00:00Z', 1800, 5000, array['race']),
                       (@userId, 'manual', '2027-01-02T12:00:00Z', 1800, 5000, array['easy']);
                insert into activities (user_id, source, started_at, duration_s, distance_m)
                select @userId, 'manual', '2028-01-01T12:00:00Z', 1800, 5000 from generate_series(1, 30)
                """, new { userId });
            var first = await ActivityList.ReadAsync(conn, userId, "UTC", ct: ct, tag: "race");
            Assert.Equal(25, first.Items.Count);
            Assert.False(first.Items[0].ShowMonth);
            Assert.False(first.Items[0].ShowWeek);
            Assert.All(first.Items.Skip(1), a => { Assert.True(a.ShowMonth); Assert.True(a.ShowWeek); });
            var cursor = Assert.IsType<ActivityCursor>(first.NextCursor);
            var second = await ActivityList.ReadAsync(conn, userId, "UTC", cursor.StartedAt, cursor.Id, ct, "race");
            Assert.Equal(2, second.Items.Count);
            Assert.Null(second.NextCursor);
            var all = first.Items.Concat(second.Items).ToArray();
            Assert.Equal(27, all.Select(a => a.Id).Distinct().Count());
            Assert.All(all, a => Assert.Equal("race", Assert.Single(a.Tags)));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }
    }

    private static async Task<Guid> Runner(NpgsqlConnection conn)
    {
        var id = Guid.NewGuid();
        await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)",
            new { id, email = $"pagination-{id:N}@example.com" });
        await conn.ExecuteAsync("insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
        return id;
    }

    private static Task<Guid> Add(NpgsqlConnection conn, Guid userId, string date) =>
        conn.QuerySingleAsync<Guid>("""
            insert into activities (user_id, source, started_at, duration_s, distance_m)
            values (@userId, 'manual', @date::timestamptz, 1800, 5000) returning id
            """, new { userId, date });
}
