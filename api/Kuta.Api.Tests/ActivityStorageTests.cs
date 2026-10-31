using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class ActivityStorageTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Single_delete_locks_the_activity_before_removing_its_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = factory.Services.GetRequiredService<NpgsqlDataSource>();
        var store = factory.Services.GetRequiredService<RawFileStore>();
        await using var owner = await db.OpenConnectionAsync(ct);
        await using var deletion = await db.OpenConnectionAsync(ct);
        var userId = Guid.NewGuid();
        var key = $"fit/{userId}/delete-race.fit";
        await owner.ExecuteAsync("""
            insert into auth.users (id, email) values (@userId, @email);
            insert into users (id, display_name) values (@userId, 'Synthetic runner')
            """, new { userId, email = $"delete-race-{userId:N}@example.com" });
        try
        {
            var id = await owner.ExecuteScalarAsync<Guid>("""
                insert into activities (user_id, source, started_at, duration_s, distance_m, raw_file_key)
                values (@userId, 'fit', now(), 600, 1800, @key) returning id
                """, new { userId, key });
            await store.PutAsync(key, "synthetic file"u8.ToArray(), ct);
            Task<bool> deleting;
            await using (var tx = await owner.BeginTransactionAsync(ct))
            {
                await owner.ExecuteAsync("select id from activities where id = @id for update", new { id }, tx);
                deleting = ActivityStorage.DeleteAsync(deletion, store, userId, id, ct);
                await DatabaseWait.UntilBlockedAsync(owner, deletion.ProcessID, ct);
                Assert.Equal(1, await owner.ExecuteScalarAsync<int>(
                    "select count(*) from storage.objects where bucket_id = 'fit-files' and name = @key", new { key }, tx));
                // A competing delete finishes while this request is waiting to read the activity.
                await owner.ExecuteAsync("delete from activities where id = @id", new { id }, tx);
                await tx.CommitAsync(ct);
            }
            Assert.False(await deleting);
            // A late request must never delete the key it read from an older copy of the activity.
            Assert.Equal(1, await owner.ExecuteScalarAsync<int>(
                "select count(*) from storage.objects where bucket_id = 'fit-files' and name = @key", new { key }));
        }
        finally
        {
            await store.DeleteAsync([key], ct);
            await owner.ExecuteAsync("delete from auth.users where id = @userId", new { userId });
        }
    }

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
