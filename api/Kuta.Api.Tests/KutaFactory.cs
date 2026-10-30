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

    // Registrerar, bekräftar via mejlet i Mailpit och skapar profilen; klienten är inloggad efteråt.
    public static async Task SignUpAsync(HttpClient client, string email, string displayName, CancellationToken ct)
    {
        await UseCsrfToken(client, ct);
        (await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password }, ct)).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/auth/confirm?token_hash={await ConfirmationTokenHash(email, ct)}", ct)).Dispose();
        await UseCsrfToken(client, ct);
        (await client.PutAsJsonAsync("/api/me", new { displayName }, ct)).EnsureSuccessStatusCode();
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
