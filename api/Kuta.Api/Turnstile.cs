using System.Net;
using System.Text.Json;

public enum TurnstileCheck { Passed, Failed, Unavailable }

// Cloudflare Turnstile: checks that a person, not a script, signs in (credential stuffing), signs up or asks for a
// password link (both send email). The site key is public and goes to the web; the secret key stays on the server, which verifies every token
// with Cloudflare. Without both keys (local development, tests) the check is skipped.
public sealed class TurnstileVerifier(HttpClient http, IConfiguration configuration)
{
    public const string Origin = "https://challenges.cloudflare.com";
    // Cloudflare's limit for a token.
    const int MaxTokenLength = 2048;

    public string? SiteKey { get; } = Configured(configuration["Turnstile:SiteKey"], configuration["Turnstile:SecretKey"]);
    readonly string? secretKey = configuration["Turnstile:SecretKey"];

    static string? Configured(string? siteKey, string? secretKey) =>
        string.IsNullOrWhiteSpace(siteKey) || string.IsNullOrWhiteSpace(secretKey) ? null : siteKey;

    // Unavailable when Cloudflare cannot answer, so the visitor is told to try again rather than that the check failed.
    public async Task<TurnstileCheck> CheckAsync(string? token, IPAddress? clientAddress, CancellationToken cancellationToken)
    {
        if (SiteKey is null) return TurnstileCheck.Passed;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) return TurnstileCheck.Failed;
        var fields = new Dictionary<string, string> { ["secret"] = secretKey!, ["response"] = token };
        if (clientAddress is not null) fields["remoteip"] = clientAddress.ToString();
        try
        {
            using var response = await http.PostAsync($"{Origin}/turnstile/v0/siteverify", new FormUrlEncodedContent(fields),
                cancellationToken);
            if (!response.IsSuccessStatusCode) return TurnstileCheck.Unavailable;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True
                ? TurnstileCheck.Passed : TurnstileCheck.Failed;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return TurnstileCheck.Unavailable;
        }
    }

    public static IResult? Refusal(TurnstileCheck check) => check switch
    {
        TurnstileCheck.Failed => Results.BadRequest(
            new ApiError("Kontrollen att du är en människa gick inte igenom. Försök igen.", "turnstileFailed")),
        TurnstileCheck.Unavailable => Results.Json(
            new ApiError("Kontrollen kan inte göras just nu. Försök igen om en stund.", "turnstileUnavailable"), statusCode: 503),
        _ => null,
    };
}
