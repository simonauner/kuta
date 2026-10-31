using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class StravaEquipmentTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Import_preserves_edits_and_links_only_missing_owned_equipment_without_double_counting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        try
        {
            foreach (var id in new[] { owner, other })
            {
                await conn.ExecuteAsync("insert into auth.users(id,email) values(@id,@email)", new { id, email = $"strava-gear-{id}@example.com" });
                await conn.ExecuteAsync("insert into users(id,display_name) values(@id,'Synthetic runner')", new { id });
            }
            StravaShoe[] shoes = [new("g1", "Same name", "Brand"), new("g2", "Same name", "Brand")];
            var imported = await StravaEquipment.ImportAsync(conn, owner, shoes, ct);
            Assert.Equal(2, imported.Imported);
            var first = imported.Ids["g1"];
            var second = imported.Ids["g2"];
            Assert.NotEqual(first, second);
            Assert.All(await EquipmentEndpoints.LoadAsync(conn, owner), s => Assert.Equal((0d, 0L), (s.DistanceM, s.DurationS)));
            await conn.ExecuteAsync("update equipment set name='Renamed', retired=true, initial_distance_m=1000, initial_duration_s=60 where id=@first", new { first });
            var retry = await StravaEquipment.ImportAsync(conn, owner, shoes, ct);
            Assert.Equal(0, retry.Imported);
            Assert.Equal(first, retry.Ids["g1"]);
            var foreign = (await StravaEquipment.ImportAsync(conn, other, shoes, ct)).Ids["g1"];
            Assert.NotEqual(first, foreign);
            var hash = new byte[] { 1, 2, 3 };
            var activity = await conn.ExecuteScalarAsync<Guid>("""
                insert into activities(user_id,source,started_at,distance_m,duration_s,strava_activity_id,raw_file_sha256)
                values(@owner,'strava',now(),5000,1800,'123',@hash) returning id
                """, new { owner, hash });
            Assert.False(await StravaEquipment.AttachMissingAsync(conn, other, foreign, "123", hash, ct));
            Assert.False(await StravaEquipment.AttachMissingAsync(conn, owner, foreign, "123", hash, ct));
            Assert.True(await StravaEquipment.AttachMissingAsync(conn, owner, first, "123", null, ct));
            Assert.False(await StravaEquipment.AttachMissingAsync(conn, owner, second, "123", null, ct));
            var gear = (await EquipmentEndpoints.LoadAsync(conn, owner)).Single(s => s.Id == first);
            Assert.Equal(("Renamed", true, 6000d, 1860L), (gear.Name, gear.Retired, gear.DistanceM, gear.DurationS));
            // A standalone FIT duplicate has no Strava ID; its raw-file hash can supply the missing link.
            await conn.ExecuteAsync("delete from activity_equipment where activity_id=@activity; update activities set strava_activity_id=null where id=@activity", new { activity });
            Assert.True(await StravaEquipment.AttachMissingAsync(conn, owner, first, "123", hash, ct));
            Assert.False(await StravaEquipment.AttachMissingAsync(conn, owner, first, "123", hash, ct));
            Assert.Equal(6000, (await EquipmentEndpoints.LoadAsync(conn, owner)).Single(s => s.Id == first).DistanceM);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id=any(@ids)", new { ids = new[] { owner, other } }); }
    }
}
