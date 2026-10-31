using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class EquipmentTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Usage_sums_linked_activities_and_initial_values_and_follows_unlinking_and_deletion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            await AddRunner(conn, ownerId);
            await AddRunner(conn, otherId);
            var first = await AddEquipment(conn, ownerId, "First", 1234.5, 3600);
            var second = await AddEquipment(conn, ownerId, "Second", 0, 0);
            var other = await AddEquipment(conn, otherId, "Private equipment", 9000, 900);
            var activity1 = await AddActivity(conn, ownerId, 5000.25, 1800);
            var activity2 = await AddActivity(conn, ownerId, 3000, 900);
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                Assert.True(await EquipmentEndpoints.OwnsAsync(conn, tx, ownerId, [first, second]));
                Assert.False(await EquipmentEndpoints.OwnsAsync(conn, tx, ownerId, [other]));
                await EquipmentEndpoints.AttachAsync(conn, tx, activity1, ownerId, [first, first, second]);
                await EquipmentEndpoints.AttachAsync(conn, tx, activity2, ownerId, [first]);
                await tx.CommitAsync(ct);
            }
            var totals = (await EquipmentEndpoints.LoadAsync(conn, ownerId)).ToDictionary(e => e.Id);
            Assert.Equal(2, totals.Count);
            Assert.Equal((9234.75, 6300L), (totals[first].DistanceM, totals[first].DurationS));
            Assert.Equal((5000.25, 1800L), (totals[second].DistanceM, totals[second].DurationS));
            // Ownership is enforced by the database too, for either side of the link.
            var error = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into activity_equipment (activity_id, equipment_id, user_id) values (@activity1, @other, @ownerId)",
                new { activity1, other, ownerId }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            error = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into activity_equipment (activity_id, equipment_id, user_id) values (@activity1, @other, @otherId)",
                new { activity1, other, otherId }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);

            await conn.ExecuteAsync("delete from activities where id = @activity1", new { activity1 });
            totals = (await EquipmentEndpoints.LoadAsync(conn, ownerId)).ToDictionary(e => e.Id);
            Assert.Equal((4234.5, 4500L), (totals[first].DistanceM, totals[first].DurationS));
            Assert.Equal((0d, 0L), (totals[second].DistanceM, totals[second].DurationS));
            await conn.ExecuteAsync("delete from activity_equipment where activity_id = @activity2", new { activity2 });
            var remaining = (await EquipmentEndpoints.LoadAsync(conn, ownerId)).Single(e => e.Id == first);
            Assert.Equal((1234.5, 3600L), (remaining.DistanceM, remaining.DurationS));
            Assert.Equal(other, Assert.Single(await EquipmentEndpoints.LoadAsync(conn, otherId)).Id);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { ownerId, otherId } }); }
    }

    [Fact]
    public async Task Equipment_can_be_created_and_linked_during_manual_and_fit_creation_or_afterwards_only_by_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            await AddRunner(conn, ownerId);
            await AddRunner(conn, otherId);
            using var owner = await Client(ownerId, ct);
            using var other = await Client(otherId, ct);
            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/equipment", ct)).StatusCode);
            using var created = await owner.PostAsJsonAsync("/api/equipment", new EquipmentRequest(" Test shoe ", " Brand ", "shoe", 1000, 600), ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var gear = (await created.Content.ReadFromJsonAsync<EquipmentSummary>(ct))!;
            Assert.Equal(("Test shoe", "Brand", "shoe", 1000d, 600L), (gear.Name, gear.Brand, gear.Kind, gear.DistanceM, gear.DurationS));
            Assert.Empty((await other.GetFromJsonAsync<EquipmentSummary[]>("/api/equipment", ct))!);
            var manual = new ManualActivityRequest("running", "Synthetic run", DateTimeOffset.UtcNow, 1800, 5000, null, null,
                EquipmentIds: [gear.Id]);
            using var response = await owner.PostAsJsonAsync("/api/activities/manual", manual, ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var activity = (await response.Content.ReadFromJsonAsync<ActivitySummary>(ct))!;
            Assert.Equal(gear.Id, Assert.Single(activity.EquipmentIds));
            Assert.Equal(gear.Id, Assert.Single((await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct))!.EquipmentIds));

            Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/activities/manual", manual, ct)).StatusCode);
            Assert.Empty((await other.GetFromJsonAsync<ActivityPage>("/api/activities", ct))!.Items);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/activities/{activity.Id}/equipment",
                new { equipmentIds = new[] { gear.Id } }, ct)).StatusCode);
            var otherGear = await AddEquipment(conn, otherId, "Private shoe", 0, 0);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/activities/{activity.Id}/equipment",
                new { equipmentIds = new[] { otherGear } }, ct)).StatusCode);
            Assert.Equal(gear.Id, Assert.Single((await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct))!.EquipmentIds));
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/activities/{activity.Id}/equipment",
                new { equipmentIds = new[] { gear.Id } }, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/activities/{activity.Id}/equipment",
                new { equipmentIds = Array.Empty<Guid>() }, ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{activity.Id}", ct))!.EquipmentIds);

            // Rejecting FIT equipment must roll back the common storage path, allowing the same file afterwards.
            using (var rejectedForm = new MultipartFormDataContent())
            {
                rejectedForm.Add(new ByteArrayContent(FitFile.Run(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), 600)), "file", "synthetic.fit");
                rejectedForm.Add(new StringContent(otherGear.ToString()), "equipmentIds");
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/activities", rejectedForm, ct)).StatusCode);
                Assert.Single((await owner.GetFromJsonAsync<ActivityPage>("/api/activities", ct))!.Items);
            }

            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(FitFile.Run(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), 600));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", "synthetic.fit");
            form.Add(new StringContent(gear.Id.ToString()), "equipmentIds");
            using var uploaded = await owner.PostAsync("/api/activities", form, ct);
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            var fit = (await uploaded.Content.ReadFromJsonAsync<ActivitySummary>(ct))!;
            Assert.Equal(gear.Id, Assert.Single(fit.EquipmentIds));
            Assert.Equal(gear.Id, Assert.Single((await owner.GetFromJsonAsync<ActivityPage>("/api/activities", ct))!.Items.Single(a => a.Id == fit.Id).EquipmentIds));
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/activities", ct)).StatusCode);
            var afterDelete = Assert.Single((await owner.GetFromJsonAsync<EquipmentSummary[]>("/api/equipment", ct))!);
            Assert.Equal((1000d, 600L), (afterDelete.DistanceM, afterDelete.DurationS));
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] { ownerId, otherId } }); }
    }

    [Fact]
    public async Task Retirement_reactivation_and_deletion_are_private_and_preserve_activities_and_last_used_selection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        try
        {
            await AddRunner(conn, ownerId);
            await AddRunner(conn, otherId);
            using var owner = await Client(ownerId, ct);
            using var other = await Client(otherId, ct);
            Assert.Null((await owner.GetFromJsonAsync<LastUsedShoe>("/api/equipment/last-used-shoe", ct))!.Id);
            var first = await AddEquipment(conn, ownerId, "Older shoe", 0, 0);
            var second = await AddEquipment(conn, ownerId, "Latest shoe", 0, 0);
            var foreign = await AddEquipment(conn, otherId, "Private shoe", 0, 0);
            var oldRun = await AddActivity(conn, ownerId, 5000, 1800);
            var recentRun = await AddActivity(conn, ownerId, 3000, 900);
            var foreignRun = await AddActivity(conn, otherId, 1000, 600);
            await conn.ExecuteAsync("update activities set started_at = now() - interval '1 day' where id = @oldRun", new { oldRun });
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                await EquipmentEndpoints.AttachAsync(conn, tx, oldRun, ownerId, [first]);
                await EquipmentEndpoints.AttachAsync(conn, tx, recentRun, ownerId, [second]);
                await EquipmentEndpoints.AttachAsync(conn, tx, foreignRun, otherId, [foreign]);
                await tx.CommitAsync(ct);
            }
            Assert.Equal(second, (await owner.GetFromJsonAsync<LastUsedShoe>("/api/equipment/last-used-shoe", ct))!.Id);
            Assert.Equal(foreign, (await other.GetFromJsonAsync<LastUsedShoe>("/api/equipment/last-used-shoe", ct))!.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/equipment/{second}/retire", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/equipment/{second}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/equipment/{second}/retire", null, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/equipment/{second}/retire", ct)).StatusCode);
            Assert.True((await EquipmentEndpoints.LoadAsync(conn, ownerId)).Single(e => e.Id == second).Retired);
            Assert.Equal(first, (await owner.GetFromJsonAsync<LastUsedShoe>("/api/equipment/last-used-shoe", ct))!.Id);
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                Assert.False(await EquipmentEndpoints.OwnsAsync(conn, tx, ownerId, [second]));
                Assert.True(await EquipmentEndpoints.OwnsAsync(conn, tx, ownerId, [second], allowRetired: true));
                await tx.RollbackAsync(ct);
            }
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/equipment/{second}/retire", ct)).StatusCode);
            Assert.False((await EquipmentEndpoints.LoadAsync(conn, ownerId)).Single(e => e.Id == second).Retired);
            Assert.Equal(second, (await owner.GetFromJsonAsync<LastUsedShoe>("/api/equipment/last-used-shoe", ct))!.Id);
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                Assert.True(await EquipmentEndpoints.OwnsAsync(conn, tx, ownerId, [second]));
                await tx.RollbackAsync(ct);
            }
            Assert.Equal(second, Assert.Single((await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{recentRun}", ct))!.EquipmentIds));
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/equipment/{second}", ct)).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<ActivityDetail>($"/api/activities/{recentRun}", ct))!.EquipmentIds);
            Assert.DoesNotContain(await EquipmentEndpoints.LoadAsync(conn, ownerId), e => e.Id == second);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/equipment/{second}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/activities/{recentRun}", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/activities/{recentRun}", ct)).StatusCode);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where id = any(@ids)", new { ids = new[] {ownerId, otherId} }); }
    }

    private static async Task AddRunner(NpgsqlConnection conn, Guid id)
    {
        await conn.ExecuteAsync("insert into auth.users (id, email) values (@id, @email)", new { id, email = $"equipment-{id:N}@example.com" });
        await conn.ExecuteAsync("insert into users (id, display_name) values (@id, 'Synthetic runner')", new { id });
    }
    private static Task<Guid> AddEquipment(NpgsqlConnection conn, Guid userId, string name, double distance, long duration) =>
        conn.ExecuteScalarAsync<Guid>("""
            insert into equipment (user_id, name, initial_distance_m, initial_duration_s)
            values (@userId, @name, @distance, @duration) returning id
            """, new { userId, name, distance, duration });
    private static Task<Guid> AddActivity(NpgsqlConnection conn, Guid userId, double distance, int duration) =>
        conn.ExecuteScalarAsync<Guid>("""
            insert into activities (user_id, source, started_at, distance_m, duration_s, moving_s)
            values (@userId, 'manual', now(), @distance, @duration, @duration - 60) returning id
            """, new { userId, distance, duration });
    private async Task<HttpClient> Client(Guid id, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await SupabaseCookieSession.SignInAsync(http, new AuthSession(id, $"equipment-{id:N}@example.com", "synthetic-access",
            "synthetic-refresh", DateTimeOffset.UtcNow.AddHours(1)), ct);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", http.Response.Headers.SetCookie.Select(c => c!.Split(';')[0])));
        await TestSession.UseCsrfToken(client, ct);
        return client;
    }
}

public class EquipmentInputTests
{
    static readonly EquipmentRequest Valid = new("Trail shoes", null, "shoe");
    [Fact]
    public void New_equipment_starts_at_zero_and_initial_usage_is_optional()
    {
        Assert.Null(EquipmentEndpoints.Validate(Valid));
        Assert.Null(EquipmentEndpoints.Validate(Valid with { InitialDistanceM = 1234.5, InitialDurationS = 3600 }));
    }
    public static TheoryData<EquipmentRequest> Invalid => new()
    {
        Valid with { Name = " " }, Valid with { Name = new string('x', 101) },
        Valid with { Brand = new string('x', 101) }, Valid with { Kind = "unknown" },
        Valid with { InitialDistanceM = -1 }, Valid with { InitialDistanceM = double.NaN },
        Valid with { InitialDistanceM = double.PositiveInfinity }, Valid with { InitialDurationS = -1 },
        Valid with { InitialDistanceM = 1_000_000_001 }, Valid with { InitialDurationS = 100L * 365 * 24 * 3600 + 1 },
    };
    [Theory, MemberData(nameof(Invalid))]
    public void Invalid_equipment_is_rejected(EquipmentRequest request) =>
        Assert.Equal("invalidEquipmentInput", EquipmentEndpoints.Validate(request)?.Code);
}
