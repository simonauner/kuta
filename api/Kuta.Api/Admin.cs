using Dapper;
using Npgsql;

// Aggregate counts only: no names, addresses or other per-account data.
public record AdminStats(long Accounts, long ConfirmedAccounts, long NewAccounts30Days, long SignedIn30Days,
    long AccountsWithActivities, long Activities, long NewActivities30Days);

// The admin view lives on a host of its own (Admin:Host, admin.kutaapp.com), so that a script injected into the app on the
// public host never runs where an admin session is valid. The session cookie is a host-only __Host- cookie, so the two
// hosts never share a session. Admins are listed by user id (Admin:UserIds), never by email address.
public static class Admin
{
    // The API the admin host serves: signing in and the admin API. /api/me is exact; its sub-routes stay on the public host.
    static readonly string[] AdminHostApi = ["/api/auth", "/api/antiforgery", "/api/turnstile", "/api/locale", "/api/health", "/api/admin"];

    public static void UseAdminHost(this WebApplication app) => app.Use((context, next) =>
    {
        var adminHost = app.Configuration["Admin:Host"];
        var path = context.Request.Path;
        var adminPath = path.StartsWithSegments("/api/admin") || path.StartsWithSegments("/app/admin");

        // Without a host of its own (Development, tests) the admin view shares the public host; elsewhere it is off.
        if (string.IsNullOrEmpty(adminHost))
            return adminPath && !app.Environment.IsDevelopment() ? NotFound(context) : next(context);

        if (!context.Request.Host.Host.Equals(adminHost, StringComparison.OrdinalIgnoreCase))
            return adminPath ? NotFound(context) : next(context);

        if (path == "/" || path == "/app" || path == "/app/")
        {
            context.Response.Redirect("/app/admin");
            return Task.CompletedTask;
        }
        if (path.StartsWithSegments("/api") && path != "/api/me" && !AdminHostApi.Any(a => path.StartsWithSegments(a)))
            return NotFound(context);
        return next(context);
    });

    static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    public static bool IsAdmin(IConfiguration configuration, Guid userId) =>
        (configuration["Admin:UserIds"] ?? "").Split(',', StringSplitOptions.TrimEntries)
            .Any(id => Guid.TryParse(id, out var admin) && admin == userId);

    public static void MapAdmin(this RouteGroupBuilder api)
    {
        // 404 rather than 403 for other accounts: the route does not reveal that it exists.
        api.MapGet("/admin/stats", async (HttpContext http, IConfiguration configuration, NpgsqlDataSource db) =>
        {
            if (!IsAdmin(configuration, http.User.GetUserId())) return Results.NotFound();
            await using var conn = await db.OpenConnectionAsync();
            return Results.Ok(await conn.QuerySingleAsync<AdminStats>("""
                select
                    (select count(*) from auth.users) as accounts,
                    (select count(*) from auth.users where email_confirmed_at is not null) as confirmed_accounts,
                    (select count(*) from auth.users where created_at > now() - interval '30 days') as new_accounts_30_days,
                    (select count(distinct user_id) from user_sessions where created_at > now() - interval '30 days') as signed_in_30_days,
                    (select count(distinct user_id) from activities) as accounts_with_activities,
                    (select count(*) from activities) as activities,
                    (select count(*) from activities where created_at > now() - interval '30 days') as new_activities_30_days
                """));
        }).RequireAuthorization();
    }
}
