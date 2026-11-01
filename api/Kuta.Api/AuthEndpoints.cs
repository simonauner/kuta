using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

public record Credentials(string? Email, string? Password);
public record SignUpRequest(string? Email, string? Password, bool AcceptTerms);
public record ApiError(string Error, string? Code = null);
public record ApiMessage(string Message);
public record AntiforgeryToken(string Token);
public record RecoveryRequest(string? Email);
public record ResetPasswordRequest(string? Password);

public static class AuthEndpoints
{
    // Datumet på web/terms.html (testas i TermsVersionTests). Ändra vid varje ny version av villkoren.
    public const string TermsVersion = "2026-11-01";

    // Klientens partition i auth-gränsen per IP. En IPv6-klient förfogar normalt över ett helt /64-nät och kan byta
    // adress inom det fritt, så hela nätet räknas som en klient.
    public static string ClientPartition(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    // Per e-postadress oberoende av IP: lösenordsgissning eller återställningsmejl mot ett konto från många adresser.
    // Ett konto kan då låsas i 15 minuter av någon annan; det är priset för att gissningar inte kan spridas ut.
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

    // Webben hämtar en ny token efter varje in- och utloggning, eftersom token är knuten till användaren.
    public static void MapAntiforgery(this RouteGroupBuilder api)
    {
        // Antiforgery sätter själv no-store på svaret
        api.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
            Results.Ok(new AntiforgeryToken(antiforgery.GetAndStoreTokens(http).RequestToken!)));

        // Alla skrivande anrop kräver token i X-CSRF-TOKEN, även inloggningen (annars kan en annan sajt
        // logga in besökaren på angriparens konto).
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

        auth.MapPost("/signup", async (SignUpRequest req, SupabaseAuthClient supabase, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new ApiError("Ange e-post och lösenord.", "credentialsRequired"));
            if (!req.AcceptTerms)
                return Results.BadRequest(new ApiError("Godkänn användarvillkoren och integritetspolicyn för att skapa ett konto.", "termsRequired"));

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

        auth.MapPost("/login", async (Credentials req, SupabaseAuthClient supabase, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
                return Results.BadRequest(new ApiError("Ange e-post och lösenord.", "credentialsRequired"));
            if (LimitPerEmail(req.Email) is { } limited) return limited;

            var (result, session) = await supabase.SignInAsync(req.Email.Trim(), req.Password, cancellationToken);
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

        auth.MapPost("/recover", async (RecoveryRequest req, SupabaseAuthClient supabase, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || !System.Net.Mail.MailAddress.TryCreate(req.Email.Trim(), out var address)
                || address.Address != req.Email.Trim())
                return Results.BadRequest(new ApiError("Ange en giltig e-postadress.", "invalidEmail"));
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

        // Länken i bekräftelsemejlet (supabase/templates/confirmation.html) landar här. Den bekräftar bara adressen och
        // loggar inte in: annars kan någon skicka länken till sitt eget konto till en annan person, som då laddar upp
        // sina pass till angriparens konto utan att märka det (login-CSRF). Supabases session återkallas direkt.
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
