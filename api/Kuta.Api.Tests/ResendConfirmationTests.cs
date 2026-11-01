using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Dapper;
using Xunit;
using static TestSession;

public class ResendConfirmationTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Resend_sends_a_new_confirmation_email_at_most_once_a_minute_without_revealing_accounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"resend-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        try
        {
            await UseCsrfToken(client, ct);
            var signup = await client.PostAsJsonAsync("/api/auth/signup",
                new { email, password = "resend-password-1", acceptTerms = true }, ct);
            Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);
            await ConfirmationTokenHash(email, ct);
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct); // Supabase's local minimum interval between emails

            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/resend", new { email }, ct)).StatusCode);
            Assert.Equal(2, await MessagesTo(email, 2, ct));

            var again = await client.PostAsJsonAsync("/api/auth/resend", new { email }, ct);
            Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
            Assert.Equal("rateLimited", (await again.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());

            // An address without an account gets the same answer
            var unknown = await client.PostAsJsonAsync("/api/auth/resend", new { email = $"none-{Guid.NewGuid():N}@example.com" }, ct);
            Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        }
        finally { await conn.ExecuteAsync("delete from auth.users where email = @email", new { email }); }
    }

    static async Task<int> MessagesTo(string email, int expected, CancellationToken ct)
    {
        using var mailpit = new HttpClient { BaseAddress = new Uri(KutaFactory.MailpitUrl) };
        var count = 0;
        for (var attempt = 0; attempt < 20 && count < expected; attempt++)
        {
            if (attempt > 0) await Task.Delay(250, ct);
            var search = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{email}", ct);
            count = search.GetProperty("messages").GetArrayLength();
        }
        return count;
    }
}
