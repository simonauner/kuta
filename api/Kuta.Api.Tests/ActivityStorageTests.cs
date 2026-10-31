using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityStorageTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Manual_creation_waits_for_consent_withdrawal_and_does_not_save_heart_rate()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var revoke = await db.OpenConnectionAsync(ct);
        await using var upload = await db.OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        await revoke.ExecuteAsync("""
            insert into auth.users (id, email) values (@userId, @email);
            insert into users (id, display_name, hr_consent_at) values (@userId, 'Synthetic runner', now())
            """, new { userId, email = $"manual-consent-{userId:N}@example.com" });
        try
        {
            Task<ActivitySummary?> saving;
            await using (var tx = await revoke.BeginTransactionAsync(ct))
            {
                await revoke.ExecuteAsync("update users set hr_consent_at = null where id = @userId", new { userId }, tx);
                saving = ActivityStorage.SaveManualAsync(upload, userId,
                    new ManualActivityRequest("running", null, DateTimeOffset.UtcNow, 600, 1800, null, 150), ct);
                await DatabaseWait.UntilBlockedAsync(revoke, upload.ProcessID, ct);
                await tx.CommitAsync(ct);
            }
            var saved = await saving;
            Assert.NotNull(saved);
            Assert.Null(saved.AvgHr);
            Assert.Null(await revoke.QuerySingleAsync<short?>("select avg_hr from activities where id = @id", new { saved.Id }));
        }
        finally { await revoke.ExecuteAsync("delete from auth.users where id = @userId", new { userId }); }
    }
}

internal static class DatabaseWait
{
    public static async Task UntilBlockedAsync(NpgsqlConnection observer, int pid, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await observer.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select cardinality(pg_blocking_pids(@pid)) > 0", new { pid }, cancellationToken: timeout.Token)))
            await Task.Delay(20, timeout.Token);
    }
}
