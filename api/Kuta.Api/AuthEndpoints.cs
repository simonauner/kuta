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

        // Länken i bekräftelsemejlet (supabase/templates/confirmation.html) landar här.
        auth.MapGet("/confirm", async ([FromQuery(Name = "token_hash")] string? tokenHash, SupabaseAuthClient supabase,
            HttpContext http, CancellationToken cancellationToken) =>
        {
            var session = string.IsNullOrWhiteSpace(tokenHash) ? null : await supabase.VerifyEmailAsync(tokenHash, cancellationToken);
            if (session is null) return Results.Redirect("/app?confirm=failed");
            await SupabaseCookieSession.SignInAsync(http, session, cancellationToken);
            return Results.Redirect("/app");
        });
    }
}
