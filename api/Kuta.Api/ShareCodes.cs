using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using Npgsql;

public record ShareCode(string Code);
public record SharedRunner(Guid RunnerId);

// Short profile links (/app/r/{code}) that the owner can replace. The code only finds the profile; blocks and
// visibility still decide what the viewer may read.
public static class ShareCodes
{
    // Look-alikes (0/o, 1/l/i) are left out; same set as the check in migration 028.
    public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    public const int Length = 8;

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static void MapShareCodes(this RouteGroupBuilder api)
    {
        // POST although it mostly reads: the first call creates the code, and GET never changes data.
        api.MapPost("/me/share-code", (NpgsqlDataSource db, ClaimsPrincipal user) => SaveAsync(db, user.GetUserId(), replace: false))
            .RequireAuthorization();
        api.MapPost("/me/share-code/replace", (NpgsqlDataSource db, ClaimsPrincipal user) => SaveAsync(db, user.GetUserId(), replace: true))
            .RequireAuthorization();
        // Rate limited so that codes cannot be enumerated.
        api.MapGet("/share-codes/{code}", async (string code, NpgsqlDataSource db) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            var runnerId = await conn.ExecuteScalarAsync<Guid?>("select id from users where share_code = @code",
                new { code = code.ToLowerInvariant() });
            return runnerId is { } id ? Results.Ok(new SharedRunner(id)) : Results.NotFound();
        }).RequireAuthorization().RequireRateLimiting("shareCodes");
    }

    private static async Task<IResult> SaveAsync(NpgsqlDataSource db, Guid userId, bool replace)
    {
        await using var conn = await db.OpenConnectionAsync();
        // 31^8 codes make a collision unlikely; retry a few times rather than fail.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var code = await conn.ExecuteScalarAsync<string?>($"""
                    update users set share_code = {(replace ? "@code" : "coalesce(share_code, @code)")}
                    where id = @userId returning share_code
                    """, new { userId, code = Generate() });
                return code is null ? Results.NotFound() : Results.Ok(new ShareCode(code));
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation && attempt < 3) { }
        }
    }
}
