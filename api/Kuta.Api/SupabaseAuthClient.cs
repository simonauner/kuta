using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// The API talks to Supabase Auth on behalf of the web app; the web app never sees tokens.

public sealed record AuthSession(Guid UserId, string Email, string AccessToken,
    string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public enum SignUpResult { SignedIn, ConfirmationSent, WeakPassword, Rejected }

public enum SignInResult { SignedIn, WrongCredentials, EmailNotConfirmed }
public enum PasswordResetResult { Updated, WeakPassword, SamePassword, Rejected }

public sealed class SupabaseAuthClient(HttpClient http, IConfiguration configuration, IHttpContextAccessor httpContext)
{
    private readonly string projectUrl = configuration["Supabase:Url"]?.TrimEnd('/')
        ?? throw new InvalidOperationException("Supabase:Url is missing");
    private readonly string publishableKey = configuration["Supabase:PublishableKey"]
        ?? throw new InvalidOperationException("Supabase:PublishableKey is missing");
    // With the secret key the visitor's IP is forwarded, so Supabase's per-IP rate limits
    // apply to the visitor and not our server (which would otherwise share one limit for all users).
    private readonly string? secretKey = configuration["Supabase:SecretKey"];

    // session is null when Supabase requires email confirmation first.
    public async Task<(SignUpResult Result, AuthSession? Session)> SignUpAsync(string email, string password,
        CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Post, "/auth/v1/signup");
        // Sign-up requires accepted terms; the timestamp is carried over to users when the profile is created.
        request.Content = JsonContent.Create(new { email, password, data = new
        {
            terms_version = AuthEndpoints.TermsVersion, terms_accepted_at = DateTime.UtcNow,
        } });
        using var response = await http.SendAsync(request, cancellationToken);
        ThrowIfUnavailable(response);
        if (!response.IsSuccessStatusCode)
            return (await ReadErrorCodeAsync(response, cancellationToken) == "weak_password"
                ? SignUpResult.WeakPassword : SignUpResult.Rejected, null);

        // With confirmation enabled Supabase responds with only the user and no session (also for a taken
        // address, so the response doesn't reveal which addresses have accounts).
        return await ReadSessionResponseAsync(response, cancellationToken) is { } session
            ? (SignUpResult.SignedIn, session) : (SignUpResult.ConfirmationSent, null);
    }

    public async Task<(SignInResult Result, AuthSession? Session)> SignInAsync(string email, string password,
        CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Post, "/auth/v1/token?grant_type=password");
        request.Content = JsonContent.Create(new { email, password });
        using var response = await http.SendAsync(request, cancellationToken);
        ThrowIfUnavailable(response);
        if (!response.IsSuccessStatusCode)
            // Supabase only says email_not_confirmed once the password is correct, so it reveals nothing to a guesser
            return (await ReadErrorCodeAsync(response, cancellationToken) == "email_not_confirmed"
                ? SignInResult.EmailNotConfirmed : SignInResult.WrongCredentials, null);
        return await ReadSessionResponseAsync(response, cancellationToken) is { } session
            ? (SignInResult.SignedIn, session) : (SignInResult.WrongCredentials, null);
    }

    // Exchanges the token_hash from the confirmation email for a session. Null when the link is invalid or used.
    public async Task<AuthSession?> VerifyEmailAsync(string tokenHash, CancellationToken cancellationToken)
        => await VerifyAsync(tokenHash, "email", cancellationToken);

    public async Task<AuthSession?> VerifyRecoveryAsync(string tokenHash, CancellationToken cancellationToken)
        => await VerifyAsync(tokenHash, "recovery", cancellationToken);

    private async Task<AuthSession?> VerifyAsync(string tokenHash, string type, CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Post, "/auth/v1/verify");
        request.Content = JsonContent.Create(new { type, token_hash = tokenHash });
        using var response = await http.SendAsync(request, cancellationToken);
        ThrowIfUnavailable(response);
        return response.IsSuccessStatusCode ? await ReadSessionResponseAsync(response, cancellationToken) : null;
    }

    public async Task RecoverAsync(string email, CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Post, "/auth/v1/recover");
        request.Content = JsonContent.Create(new { email });
        using var response = await http.SendAsync(request, cancellationToken);
        // The email cooldown must not reveal which addresses have accounts.
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return;
        ThrowIfUnavailable(response);
        // Same response for unknown addresses and rejected emails too, so accounts aren't revealed.
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(string accessToken, string password,
        CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Put, "/auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { password });
        using var response = await http.SendAsync(request, cancellationToken);
        ThrowIfUnavailable(response);
        if (response.IsSuccessStatusCode) return PasswordResetResult.Updated;
        return await ReadErrorCodeAsync(response, cancellationToken) switch
        {
            "weak_password" => PasswordResetResult.WeakPassword,
            "same_password" => PasswordResetResult.SamePassword,
            _ => PasswordResetResult.Rejected,
        };
    }

    // Null when Supabase rejects the refresh token. Network and server errors throw, so the caller can tell
    // "revoked" from "temporarily unavailable".
    public async Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token");
        request.Content = JsonContent.Create(new { refresh_token = refreshToken });
        using var response = await http.SendAsync(request, cancellationToken);
        ThrowIfUnavailable(response);
        return response.IsSuccessStatusCode ? await ReadSessionResponseAsync(response, cancellationToken) : null;
    }

    // True/false whether the session is still valid at Supabase (the user exists, same email);
    // null when Supabase could not be reached.
    public async Task<bool?> ValidateAsync(string accessToken, Guid expectedId, string expectedEmail,
        CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Get, "/auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (IsUnavailable(response.StatusCode)) return null;
            if (!response.IsSuccessStatusCode) return false;
            using var document = await ParseAsync(response, cancellationToken);
            return TryReadConfirmedUser(document.RootElement, out var id, out var email) && id == expectedId &&
                string.Equals(email, expectedEmail, StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }
    }

    // Revokes the refresh token so the session cannot be renewed. Errors are ignored: our own session is already ended.
    public async Task SignOutAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = ClientRequest(HttpMethod.Post, "/auth/v1/logout?scope=local");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try { using var response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    private HttpRequestMessage ClientRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, projectUrl + path);
        if (secretKey is not null && httpContext.HttpContext?.Connection.RemoteIpAddress is { } address)
        {
            request.Headers.Add("apikey", secretKey);
            request.Headers.Add("Sb-Forwarded-For", (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString());
        }
        else
            request.Headers.Add("apikey", publishableKey);
        return request;
    }

    private static async Task<AuthSession?> ReadSessionResponseAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await ParseAsync(response, cancellationToken);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("user", out var user)
            ? ReadSession(root, user) : null;
    }

    private static AuthSession? ReadSession(JsonElement root, JsonElement user)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryReadConfirmedUser(user, out var id, out var email) ||
            !TryReadString(root, "access_token", out var accessToken) ||
            !TryReadString(root, "refresh_token", out var refreshToken) ||
            !root.TryGetProperty("expires_in", out var expiresValue) ||
            !expiresValue.TryGetInt32(out var expiresIn) || expiresIn <= 0)
            return null;
        return new AuthSession(id, email, accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

    private static bool IsUnavailable(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

    private static void ThrowIfUnavailable(HttpResponseMessage response)
    {
        if (IsUnavailable(response.StatusCode))
            throw new HttpRequestException("Supabase Auth is unavailable.", null, response.StatusCode);
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await ParseAsync(response, cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                TryReadString(document.RootElement, "error_code", out var code) ? code : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        value = "";
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    // An address without email_confirmed_at is not proven to be the user's.
    private static bool TryReadConfirmedUser(JsonElement user, out Guid id, out string email)
    {
        id = default;
        email = "";
        return TryReadString(user, "id", out var idText) && Guid.TryParse(idText, out id) &&
            TryReadString(user, "email", out email) &&
            TryReadString(user, "email_confirmed_at", out var confirmed) &&
            DateTimeOffset.TryParse(confirmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
    }
}
