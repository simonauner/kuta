using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

// Kräver lokal Supabase (`supabase start`): användaren skapas på riktigt i Supabase Auth.
public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    const string SupabaseUrl = "http://127.0.0.1:55321";
    // Supabase CLI:s lokala standardnyckel, samma för alla installationer och avsedd att vara publik
    const string PublishableKey = "sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH";

    [Fact]
    public async Task Supabase_token_gives_access_to_own_profile_and_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/activities", ct)).StatusCode);

            var token = await SignUp(email, ct);
            client.DefaultRequestHeaders.Authorization = new("Bearer", Tamper(token));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", ct)).StatusCode);

            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var me = await client.GetFromJsonAsync<JsonElement>("/api/me", ct);
            Assert.Equal(email, me.GetProperty("email").GetString());
            Assert.Equal(JsonValueKind.Null, me.GetProperty("displayName").ValueKind);

            var bad = await client.PutAsJsonAsync("/api/me", new { displayName = " " }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

            var put = await client.PutAsJsonAsync("/api/me", new { displayName = "Testlöpare" }, ct);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            me = await client.GetFromJsonAsync<JsonElement>("/api/me", ct);
            Assert.Equal("Testlöpare", me.GetProperty("displayName").GetString());

            await conn.ExecuteAsync(
                "insert into activities (user_id, source, started_at, duration_s, distance_m) values (@id, 'manual', now(), 1800, 5000)",
                new { id = me.GetProperty("id").GetGuid() });
            var activities = await client.GetStringAsync("/api/activities", ct);
            Assert.Contains("\"startedAt\"", activities);
            Assert.Contains("\"distanceM\":5000", activities);
        }
        finally
        {
            // Kaskaderar till users och activities
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }
    }

    static async Task<string> SignUp(string email, CancellationToken ct)
    {
        using var supabase = new HttpClient();
        supabase.DefaultRequestHeaders.Add("apikey", PublishableKey);
        var res = await supabase.PostAsJsonAsync($"{SupabaseUrl}/auth/v1/signup",
            new { email, password = "hemligt-lösen" }, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }

    // Byter ett tecken mitt i signaturen
    static string Tamper(string jwt)
    {
        var i = jwt.LastIndexOf('.') + 10;
        return jwt[..i] + (jwt[i] == 'A' ? 'B' : 'A') + jwt[(i + 1)..];
    }
}
