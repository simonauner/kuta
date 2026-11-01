using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

public sealed record GarminTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt);

public sealed class GarminUnavailableException() : Exception("garminUnavailable");

// Garmin Connect Developer Program with OAuth2 PKCE. Everything happens server to server; the client secret and the
// user's tokens never reach the web app.
public sealed class GarminClient(HttpClient http, IConfiguration configuration)
{
    public const string AuthorizeUrl = "https://connect.garmin.com/oauth2Confirm";
    const string TokenUrl = "https://diauth.garmin.com/di-oauth2-service/oauth/token";
    const string UserApi = "https://apis.garmin.com/partner-gateway/rest/user";
    public const string ActivityExport = "ACTIVITY_EXPORT";
    public const int MaxFileBytes = 20 * 1024 * 1024;

    public string? ClientId => configuration["Garmin:ClientId"];
    string? ClientSecret => configuration["Garmin:ClientSecret"];
    public bool Configured => ClientId is { Length: > 0 } && ClientSecret is { Length: > 0 };

    // Before launch only listed accounts may connect; Garmin:OpenToAll opens the connection to everyone. Existing
    // connections keep receiving data and can always be disconnected.
    public bool AvailableTo(Guid userId) => Configured && (configuration.GetValue("Garmin:OpenToAll", false)
        || (configuration["Garmin:AllowedUserIds"] ?? "").Split(',', StringSplitOptions.TrimEntries)
            .Any(id => Guid.TryParse(id, out var allowed) && allowed == userId));

    sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token_expires_in")] int RefreshTokenExpiresIn);
    sealed record UserId(string? userId);
    sealed record Permissions(string[]? permissions);

    // PKCE: the verifier stays with Kuta, Garmin only gets its SHA-256.
    public static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public Task<GarminTokens?> ExchangeCodeAsync(string code, string verifier, string redirectUri, CancellationToken ct) =>
        TokenAsync([new("grant_type", "authorization_code"), new("code", code), new("code_verifier", verifier),
            new("redirect_uri", redirectUri)], ct);

    public Task<GarminTokens?> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync([new("grant_type", "refresh_token"), new("refresh_token", refreshToken)], ct);

    // null when Garmin rejects the code or refresh token (for example revoked or expired).
    async Task<GarminTokens?> TokenAsync(KeyValuePair<string, string>[] fields, CancellationToken ct)
    {
        if (!Configured) return null;
        using var content = new FormUrlEncodedContent(
            [.. fields, new("client_id", ClientId!), new("client_secret", ClientSecret!)]);
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = content }, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new GarminUnavailableException();
        var now = DateTimeOffset.UtcNow;
        // Garmin recommends a margin before expiry.
        return new GarminTokens(token.AccessToken, token.RefreshToken, now.AddSeconds(token.ExpiresIn - 600),
            now.AddSeconds(token.RefreshTokenExpiresIn - 600));
    }

    public async Task<string> GetUserIdAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(Authorized(HttpMethod.Get, $"{UserApi}/id", accessToken), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserId>(ct))?.userId is { Length: > 0 } id
            ? id : throw new GarminUnavailableException();
    }

    public async Task<string[]> GetPermissionsAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(Authorized(HttpMethod.Get, $"{UserApi}/permissions", accessToken), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Permissions>(ct))?.permissions ?? [];
    }

    // Garmin requires this call when the user disconnects or deletes the account in Kuta.
    public async Task DeleteRegistrationAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(Authorized(HttpMethod.Delete, $"{UserApi}/registration", accessToken), ct);
        // 401/403: the registration is already gone at Garmin.
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            response.EnsureSuccessStatusCode();
    }

    // Callback URLs from push are valid for 24 hours and a single download (410 afterwards). null = cannot be downloaded.
    public async Task<byte[]?> DownloadAsync(string callbackUrl, string accessToken, CancellationToken ct)
    {
        if (!IsGarminUrl(callbackUrl)) return null;
        using var response = await SendAsync(Authorized(HttpMethod.Get, callbackUrl, accessToken), ct,
            HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound or HttpStatusCode.Forbidden
            or HttpStatusCode.PreconditionFailed) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxFileBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // Push calls are unsigned, so Kuta only downloads from Garmin's API over https.
    public static bool IsGarminUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "apis.garmin.com" && uri.IsDefaultPort;

    static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using (request)
        {
            try
            {
                var response = await http.SendAsync(request, completion, ct);
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    response.Dispose();
                    throw new GarminUnavailableException();
                }
                return response;
            }
            catch (HttpRequestException) { throw new GarminUnavailableException(); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new GarminUnavailableException(); }
        }
    }
}
