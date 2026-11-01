using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Testerna kör mot standardinstansen (portar från 55300) eller, med KUTA_PORTS=BAS, mot en egen instans från
// `scripts/run-dev.sh --ports BAS`, så att en worktree inte behöver låna eller ändra någon annans Supabase.
public sealed class KutaFactory : WebApplicationFactory<Program>
{
    public static readonly int Base =
        int.TryParse(Environment.GetEnvironmentVariable("KUTA_PORTS"), out var b) ? b : 55300;
    public static string SupabaseUrl => $"http://127.0.0.1:{Base + 21}";
    public static string MailpitUrl => $"http://127.0.0.1:{Base + 24}";

    static KutaFactory()
    {
        // Miljövariabler når Program.cs redan före Build, till skillnad från inställningar via WithWebHostBuilder
        Environment.SetEnvironmentVariable("ConnectionStrings__Default",
            $"Host=127.0.0.1;Port={Base + 22};Database=postgres;Username=postgres;Password=postgres");
        Environment.SetEnvironmentVariable("Supabase__Url", SupabaseUrl);
        Environment.SetEnvironmentVariable("Storage__ServiceUrl", $"{SupabaseUrl}/storage/v1/s3");
        // Testerna kör köade jobb själva (JobWorker.RunAllAsync), så att flera testappar inte tar varandras jobb.
        Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
    }
}

public static class TestSession
{
    public const string Password = "hemligt-lösen";

    public static async Task UseCsrfToken(HttpClient client, CancellationToken ct)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery", ct);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }

    // Förbered aktivitetstester under HTTP-nivån; bara auth-testerna behöver registrering och mejl.
    public static async Task CreateRunnerAsync(KutaFactory factory, HttpClient client, NpgsqlConnection conn,
        string email, string displayName, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition("""
            insert into auth.users (id, email) values (@id, @email);
            insert into users (id, display_name) values (@id, @displayName)
            """, new { id, email, displayName }, cancellationToken: ct));
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await SupabaseCookieSession.SignInAsync(http, new AuthSession(id, email, "synthetic-access", "synthetic-refresh",
            DateTimeOffset.UtcNow.AddHours(1)), ct);
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", http.Response.Headers.SetCookie.Select(c => c!.Split(';')[0])));
        await UseCsrfToken(client, ct);
    }

    public static async Task<string> ConfirmationTokenHash(string email, CancellationToken ct)
    {
        using var mailpit = new HttpClient { BaseAddress = new Uri(KutaFactory.MailpitUrl) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var search = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{email}", ct);
            if (search.GetProperty("messages").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } message)
            {
                var full = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/message/{message.GetProperty("ID").GetString()}", ct);
                var match = Regex.Match(full.GetProperty("HTML").GetString()!, "token_hash=([0-9a-f]+)");
                Assert.True(match.Success,
                    "Mejlet saknar vår länk. Supabase har laddat standardmallen; starta om auth-containern (supabase_auth_<projekt>).");
                return match.Groups[1].Value;
            }
            await Task.Delay(250, ct);
        }
        throw new TimeoutException($"Inget bekräftelsemejl till {email} i Mailpit ({KutaFactory.MailpitUrl}).");
    }
}
