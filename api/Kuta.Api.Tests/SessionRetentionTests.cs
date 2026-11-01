using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class SessionRetentionTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Scheduled_cleanup_keeps_sessions_until_thirty_days_after_the_first_end_or_expiry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var job = await conn.QuerySingleAsync<CleanupJob>(new CommandDefinition("""
            select schedule, command, active from cron.job where jobname = 'kuta-session-retention'
            """, cancellationToken: ct));
        Assert.True(job.Active);
        Assert.Equal("0 * * * *", job.Schedule);

        // Alla data förbereds i samma transaktion: now() är stabilt även exakt på gränsen.
        await using var tx = await conn.BeginTransactionAsync(ct);
        var userId = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition("""
            insert into auth.users (id, email) values (@userId, @email)
            """, new { userId, email = $"session-retention-{userId:N}@example.com" }, tx, cancellationToken: ct));
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        await conn.ExecuteAsync(new CommandDefinition("""
            insert into user_sessions (id, user_id, expires_at, ended_at) values
              (@active, @userId, now() + interval '1 day', null),
              (@recentEnd, @userId, now() + interval '1 day', now() - interval '30 days' + interval '1 second'),
              (@recentExpiry, @userId, now() - interval '30 days' + interval '1 second', null),
              (@endAtBoundary, @userId, now() + interval '1 day', now() - interval '30 days'),
              (@expiryAtBoundary, @userId, now() - interval '30 days', null),
              (@expiredBeforeEnd, @userId, now() - interval '31 days', now() - interval '1 day')
            """, new
            {
                userId, active = ids[0], recentEnd = ids[1], recentExpiry = ids[2],
                endAtBoundary = ids[3], expiryAtBoundary = ids[4], expiredBeforeEnd = ids[5],
            }, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(job.Command, transaction: tx, cancellationToken: ct));

        var remaining = (await conn.QueryAsync<Guid>(new CommandDefinition(
            "select id from user_sessions where user_id = @userId", new { userId }, tx,
            cancellationToken: ct))).ToHashSet();
        Assert.True(remaining.SetEquals(ids.Take(3)));
        // Disposing the transaction rolls back both fixtures and the cleanup.
    }

    private sealed record CleanupJob(string Schedule, string Command, bool Active);
}
