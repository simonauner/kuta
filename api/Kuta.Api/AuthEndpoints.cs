using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

public record Credentials(string? Email, string? Password, string? TurnstileToken = null);
public record SignUpRequest(string? Email, string? Password, bool AcceptTerms, string? TurnstileToken = null);
public record ApiError(string Error, string? Code = null);
public record ApiMessage(string Message);
public record AntiforgeryToken(string Token);
public record RecoveryRequest(string? Email, string? TurnstileToken = null);
public record ResetPasswordRequest(string? Password);

public static class AuthEndpoints
{
    // The date on web/terms.html (tested in TermsVersionTests). Change with every new version of the terms.
    public const string TermsVersion = "2026-11-01";

    // Longer than Supabase's password check (bcrypt) plus network, so unknown addresses do not respond faster.
    static readonly TimeSpan FailedLoginDuration = TimeSpan.FromMilliseconds(600);

    // The client's partition in the per-IP auth limit. An IPv6 client normally controls a whole /64 network and can change
    // address freely within it, so the whole network counts as one client.
    public static string ClientPartition(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    // Per email address regardless of IP: password guessing or reset emails against one account from many addresses.
    // Someone else can then lock an account for 15 minutes; that is the price for guesses not being spreadable.
    static readonly PartitionedRateLimiter<string> PerEmail = PartitionedRateLimiter.Create<string, string>(email =>
        RateLimitPartition.GetFixedWindowLimiter(email, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(15),
        }));

    public static IResult? LimitPerEmail(string email)
    {
        using var lease = PerEmail.AttemptAcquire(email.Trim().ToLowerInvariant());
        return lease.IsAcquired ? null
            : Results.Json(new ApiError("För många försök. Försök igen om en stund.", "rateLimited"), statusCode: 429);
    }

    // The web fetches a new token after every login and logout, since the token is bound to the user.
    public static void MapAntiforgery(this RouteGroupBuilder api)
    {
        // Antiforgery sets no-store on the response itself
        // The public Turnstile site key for the sign-in, sign-up and password forms; null when the check is off.
        api.MapGet("/turnstile", (TurnstileVerifier turnstile) => Results.Ok(new { siteKey = turnstile.SiteKey }));

        api.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
            Results.Ok(new AntiforgeryToken(antiforgery.GetAndStoreTokens(http).RequestToken!)));

        // All writing calls require the token in X-CSRF-TOKEN, including login (otherwise another site can
        // sign the visitor in to the attacker's account).
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) return await next(context);
            try
            {
                await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Json(new ApiError("Säkerhetskontrollen misslyckades. Ladda om sidan.", "csrfFailed"), statusCode: 400);
            }
            return await next(context);
        });
    }

    public static void MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception exception) when (exception is HttpRequestException ||
                exception is OperationCanceledException && !context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                return Results.Json(new ApiError("Inloggningen är tillfälligt otillgänglig. Försök igen om en stund.", "authUnavailable"),
                    statusCode: 503);
            }
        });

        auth.MapPost("/signup", async (SignUpRequest req, SupabaseAuthClient supabase, TurnstileVerifier turnstile,
            HttpContext http, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new ApiError("Ange e-post och lösenord.", "credentialsRequired"));
            if (!req.AcceptTerms)
                return Results.BadRequest(new ApiError("Godkänn användarvillkoren och integritetspolicyn för att skapa ett konto.", "termsRequired"));
            if (TurnstileVerifier.Refusal(await turnstile.CheckAsync(req.TurnstileToken, http.Connection.RemoteIpAddress,
                    cancellationToken)) is { } refusal)
                return refusal;

            var (result, session) = await supabase.SignUpAsync(req.Email.Trim(), req.Password, cancellationToken);
            switch (result)
            {
                case SignUpResult.SignedIn:
                    await SupabaseCookieSession.SignInAsync(http, session!, cancellationToken);
                    return Results.NoContent();
                case SignUpResult.ConfirmationSent:
                    return Results.Accepted(null,
                        new ApiMessage("Kolla din e-post och klicka på länken för att aktivera kontot."));
                case SignUpResult.WeakPassword:
                    return Results.BadRequest(new ApiError("Lösenordet är för svagt. Använd minst 8 tecken.", "weakPassword"));
                default:
                    return Results.BadRequest(new ApiError("Kunde inte skapa kontot. Kontrollera e-postadressen.", "signupFailed"));
            }
        });

        auth.MapPost("/login", async (Credentials req, SupabaseAuthClient supabase, TurnstileVerifier turnstile, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new ApiError("Ange e-post och lösenord.", "credentialsRequired"));
            if (LimitPerEmail(req.Email) is { } limited) return limited;
            // Credential stuffing from many addresses would otherwise only meet the per-IP and per-email limits.
            if (TurnstileVerifier.Refusal(await turnstile.CheckAsync(req.TurnstileToken, http.Connection.RemoteIpAddress,
                    cancellationToken)) is { } refused)
                return refused;

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var (result, session) = await supabase.SignInAsync(req.Email.Trim(), req.Password, cancellationToken);
            // Supabase skips the password check for unknown addresses and then responds faster. Failed attempts
            // therefore take at least as long, so the response time does not reveal which addresses have an account.
            if (result != SignInResult.SignedIn
                && FailedLoginDuration - System.Diagnostics.Stopwatch.GetElapsedTime(started) is { Ticks: > 0 } remaining)
                await Task.Delay(remaining, cancellationToken);
            switch (result)
            {
                case SignInResult.SignedIn:
                    await SupabaseCookieSession.SignInAsync(http, session!, cancellationToken);
                    return Results.NoContent();
                case SignInResult.EmailNotConfirmed:
                    return Results.Json(new ApiError("Bekräfta din e-postadress först – kolla din inkorg.", "emailNotConfirmed"), statusCode: 403);
                default:
                    return Results.Json(new ApiError("Fel e-post eller lösenord.", "invalidCredentials"), statusCode: 401);
            }
        });

        auth.MapPost("/logout", async (HttpContext http, CancellationToken cancellationToken) =>
        {
            await SupabaseCookieSession.SignOutAsync(http, cancellationToken);
            return Results.NoContent();
        });

        auth.MapPost("/recover", async (RecoveryRequest req, SupabaseAuthClient supabase, TurnstileVerifier turnstile,
            HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || !System.Net.Mail.MailAddress.TryCreate(req.Email.Trim(), out var address)
                || address.Address != req.Email.Trim())
                return Results.BadRequest(new ApiError("Ange en giltig e-postadress.", "invalidEmail"));
            if (TurnstileVerifier.Refusal(await turnstile.CheckAsync(req.TurnstileToken, http.Connection.RemoteIpAddress, ct))
                is { } refusal)
                return refusal;
            if (LimitPerEmail(address.Address) is { } limited) return limited;
            await supabase.RecoverAsync(address.Address, ct);
            return Results.Accepted(null, new ApiMessage("Om adressen har ett konto får du ett mejl med en länk för att återställa lösenordet."));
        });

        auth.MapGet("/reset-password", async ([FromQuery(Name = "token_hash")] string? tokenHash,
            SupabaseAuthClient supabase, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var session = string.IsNullOrWhiteSpace(tokenHash) ? null : await supabase.VerifyRecoveryAsync(tokenHash, ct);
            if (session is null) return Results.Redirect("/app?reset=failed");
            await PasswordRecovery.StartAsync(http, session, ct);
            return Results.Redirect("/app?reset=password");
        });

        auth.MapPost("/reset-password", (ResetPasswordRequest req, HttpContext http, SupabaseAuthClient supabase,
            Npgsql.NpgsqlDataSource db, CancellationToken ct) => PasswordRecovery.ResetAsync(req.Password, http, supabase, db, ct));

        // The link in the confirmation email (supabase/templates/confirmation.html) lands here. It only confirms the address and
        // does not sign in: otherwise someone could send the link for their own account to another person, who would then upload
        // their activities to the attacker's account without noticing (login CSRF). The Supabase session is revoked immediately.
        auth.MapGet("/confirm", async ([FromQuery(Name = "token_hash")] string? tokenHash, SupabaseAuthClient supabase,
            CancellationToken cancellationToken) =>
        {
            var session = string.IsNullOrWhiteSpace(tokenHash) ? null : await supabase.VerifyEmailAsync(tokenHash, cancellationToken);
            if (session is null) return Results.Redirect("/app?confirm=failed");
            await supabase.SignOutAsync(session.AccessToken, cancellationToken);
            return Results.Redirect("/app?confirm=ok");
        });
    }
}
