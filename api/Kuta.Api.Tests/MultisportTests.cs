using System.Net;
using System.Net.Http.Json;
using Dapper;
using Dynastream.Fit;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class MultisportCalculationTests
{
    [Fact]
    public void Cut_boundaries_interpolate_distance_and_never_join_running_across_other_sports()
    {
        int[] t = [0, 100, 200, 300];
        float?[] dist = [0, 1000, 2000, 3000];
        var req = new MultisportRequest("swimrun", [new("running", 0, 75, true), new("swimming", 75, 225, true), new("running", 225, 300, true)]);
        var parts = Multisport.Build(req, t, dist)!;
        Assert.Equal(new double[] { 750, 1500, 750 }, parts.Select(p => p.DistanceM));
        Assert.Empty(BestEfforts.Calculate("swimrun", t, dist, parts));
        parts = Multisport.Build(new("multisport", [new("cycling", 0, 150, false), new("running", 150, 300, true)]), t, dist)!;
        var effort = Assert.Single(BestEfforts.Calculate("triathlon", t, dist, parts));
        Assert.Equal(1000, effort.DistanceM);
        Assert.Equal(150, effort.StartOffsetS);
        Assert.Equal(100, effort.ElapsedS);
    }

    [Fact]
    public void Invalid_boundaries_and_broken_streams_are_rejected_without_guessing()
    {
        int[] t = [0, 100, 200];
        float?[] dist = [0, 1000, 2000];
        ActivityPartInput[] valid = [new("running", 0, 100, true), new("swimming", 100, 200, true)];
        Assert.Null(Multisport.Build(new("unknown", valid), t, dist));
        Assert.Null(Multisport.Build(new("swimrun", [valid[0], valid[1] with { StartS = 99 }]), t, dist));
        Assert.Null(Multisport.Build(new("swimrun", [valid[0], valid[1] with { EndS = 199 }]), t, dist));
        Assert.Null(Multisport.Build(new("swimrun", [valid[0], valid[1] with { Sport = "invalid" }]), t, dist));
        Assert.False(Multisport.CanSplit(t, [0, null, 2000]));
        Assert.False(Multisport.CanSplit(t, [0, 1000, 0]));
        Assert.False(Multisport.CanSplit([0, 0, 200], dist));
        Assert.Null(Multisport.Build(new("swimrun", null!), t, dist));
    }

    [Fact]
    public void Strava_keeps_native_multisport_clock_and_summary_but_offsets_single_sport_streams()
    {
        var summary = StravaFixture.Summary();
        var fit = summary with { StartedAt = summary.StartedAt.AddSeconds(30), T = [0, 100], DistanceM = 1000 };
        var single = StravaImport.MergeFit(summary, fit);
        Assert.Equal(summary.DistanceM, single.DistanceM);
        Assert.Equal(new[] { 30, 130 }, single.T);
        var multi = fit with { Sport = "swimrun", Parts = [new(0, "running", 0, 100, 1000, true)] };
        Assert.Equal(multi, StravaImport.MergeFit(summary, multi));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fit_preserves_sport_sessions_and_normalizes_reset_or_cumulative_distance(bool cumulative)
    {
        using var stream = new MemoryStream();
        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);
        var file = new FileIdMesg();
        file.SetType(Dynastream.Fit.File.Activity);
        encoder.Write(file);
        var start = new System.DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var leg = 0; leg < 3; leg++)
        {
            var sport = new[] { Sport.Swimming, Sport.Cycling, Sport.Running }[leg];
            for (var i = 0; i < 100; i++)
            {
                var r = new RecordMesg();
                r.SetTimestamp(new Dynastream.Fit.DateTime(start.AddSeconds(leg * 100 + i)));
                r.SetDistance((cumulative ? leg * 1000 : 0) + i * 10);
                r.SetCadence(80);
                r.SetHeartRate(150);
                encoder.Write(r);
            }
            var session = new SessionMesg();
            session.SetStartTime(new Dynastream.Fit.DateTime(start.AddSeconds(leg * 100)));
            session.SetTimestamp(new Dynastream.Fit.DateTime(start.AddSeconds((leg + 1) * 100)));
            session.SetSport(sport);
            session.SetTotalElapsedTime(100);
            session.SetTotalDistance(1000);
            encoder.Write(session);
        }
        encoder.Close();
        var parsed = FitImport.Parse(stream.ToArray())!;
        Assert.Equal("triathlon", parsed.Sport);
        Assert.Equal(300, parsed.DurationS);
        Assert.Equal(3000, parsed.DistanceM);
        Assert.Equal(new[] { "swimming", "cycling", "running" }, parsed.Parts!.Select(p => p.Sport));
        Assert.Equal((short)160, parsed.Cadence![^2]!.Value);
        Assert.Equal(3000f, parsed.Dist![^1]!.Value);
        Assert.True(Multisport.CanSplit(parsed.T, parsed.Dist));
        Assert.Null(parsed.WithoutHeartRate().Hr);
        Assert.Equal(parsed.Parts, parsed.WithoutHeartRate().Parts);
    }
}

public class MultisportStorageTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Edit_and_restore_are_atomic_preserve_original_and_update_equipment_and_records()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        var email = $"parts-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"parts-other-{Guid.NewGuid():N}@example.com";
        await TestSession.CreateRunnerAsync(factory, owner, conn, email, "Synthetic runner", ct);
        await TestSession.CreateRunnerAsync(factory, other, conn, otherEmail, "Other runner", ct);
        var userId = await conn.QuerySingleAsync<Guid>("select id from auth.users where email = @email", new { email });
        try
        {
            var id = await conn.ExecuteScalarAsync<Guid>("""
                insert into activities (user_id, source, sport, started_at, duration_s, distance_m)
                values (@userId, 'strava', 'running', '2026-01-01Z', 300, 3200) returning id
                """, new { userId });
            await conn.ExecuteAsync("insert into activity_streams (activity_id,t,dist) values (@id, array[0,100,200,300], array[0,1000,2000,3000]::real[])", new { id });
            var shoe = await conn.ExecuteScalarAsync<Guid>("insert into equipment (user_id, name) values (@userId, 'Test shoe') returning id", new { userId });
            await conn.ExecuteAsync("insert into activity_equipment values (@id, @shoe, @userId)", new { id, shoe, userId });
            var req = new MultisportRequest("multisport", [new("cycling", 0, 150, false), new("running", 150, 300, true)]);
            Assert.Equal(404, await Multisport.UpdateAsync(conn, Guid.NewGuid(), id, req));
            Assert.Equal(204, await Multisport.UpdateAsync(conn, userId, id, req));
            Assert.Equal("multisport", await conn.QuerySingleAsync<string>("select sport from activities where id = @id", new { id }));
            Assert.Equal(1500, (await EquipmentEndpoints.LoadAsync(conn, userId)).Single().DistanceM);
            Assert.Equal(150, (await EquipmentEndpoints.LoadAsync(conn, userId)).Single().DurationS);
            Assert.Equal(150, await conn.QuerySingleAsync<double>("select start_offset_s from best_efforts where activity_id = @id", new { id }));
            Assert.Equal(400, await Multisport.UpdateAsync(conn, userId, id, req with { Kind = "bad" }));
            Assert.Equal(2, (await Multisport.LoadAsync(conn, id)).Length);
            Assert.Equal(204, await Multisport.UpdateAsync(conn, userId, id, req with { Parts = req.Parts.Select(p => p with { UseEquipment = false }).ToArray() }));
            Assert.Equal(0, (await EquipmentEndpoints.LoadAsync(conn, userId)).Single().DistanceM);
            // HTTP adds binding, response shape and feature-specific access checks; calculation cases are above.
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/activities/{id}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/activities/{id}/parts", req, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/activities/{id}/parts", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/activities/{id}/parts", req, ct)).StatusCode);
            var detail = await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{id}", ct);
            Assert.True(detail!.PartsEdited);
            Assert.True(detail.CanSplit);
            Assert.Equal(2, detail.Parts.Length);
            Assert.Equal(3000, detail.DistanceM);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/activities/{id}/parts", ct)).StatusCode);
            Assert.Equal("running", await conn.QuerySingleAsync<string>("select sport from activities where id = @id", new { id }));
            Assert.Empty(await Multisport.LoadAsync(conn, id));
            Assert.Equal(3200, (await EquipmentEndpoints.LoadAsync(conn, userId)).Single().DistanceM);
            Assert.Equal(2, await conn.QuerySingleAsync<int>("select count(*) from best_efforts where activity_id = @id", new { id }));
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                Assert.Equal(new[] { 0,100,200,300 }, (await Multisport.ReadStreamAsync(conn, tx, id)).T);
                await Multisport.SavePartsAsync(conn, tx, id,
                    [new(0, "running", 0, 100, 1000, true), new(1, "swimming", 100, 300, 2000, true)], true);
                await conn.ExecuteAsync("update activities set sport = 'swimrun', original_sport = null where id = @id", new { id }, tx);
                await tx.CommitAsync(ct);
            }
            Assert.Equal(204, await Multisport.UpdateAsync(conn, userId, id, req));
            Assert.Equal(204, await Multisport.UpdateAsync(conn, userId, id, null));
            Assert.Equal("swimrun", await conn.QuerySingleAsync<string>("select sport from activities where id = @id", new { id }));
            Assert.Equal(new[] { "running", "swimming" }, (await Multisport.LoadAsync(conn, id)).Select(p => p.Sport));
            await conn.ExecuteAsync("delete from activities where id = @id", new { id });
            Assert.Equal(0, await conn.QuerySingleAsync<int>("select count(*) from activity_parts where activity_id = @id", new { id }));
            Assert.True(await conn.QuerySingleAsync<bool>("select relrowsecurity from pg_class where oid = 'activity_parts'::regclass"));
            Assert.False(await conn.QuerySingleAsync<bool>("select has_table_privilege('authenticated', 'activity_parts', 'SELECT')"));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email in (@email, @otherEmail)", new { email, otherEmail });
        }
    }
}
