using System.Security.Claims;
using Dapper;
using Npgsql;

// Profile pictures: the web scales and crops to a square JPEG before upload, which also drops EXIF (GPS). The picture
// is shown wherever the name is, so it has no visibility of its own; the random id in the URL is not guessable.
public static class Avatars
{
    const int MaxBytes = 200 * 1024;

    public static string Key(Guid avatarId) => $"avatars/{avatarId}.jpg";

    public static void MapAvatars(this RouteGroupBuilder api)
    {
        // shortcut: served through the API with a year of browser cache; move to a public bucket if the traffic grows.
        api.MapGet("/avatars/{id:guid}", async (Guid id, NpgsqlDataSource db, RawFileStore store, HttpContext http,
            CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            // Only the current picture: a replaced or deleted one stops loading even if removing its file failed.
            if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from users where avatar_id = @id)", new { id }, cancellationToken: ct)))
                return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Stream(stream => store.CopyToAsync(Key(id), stream, ct), "image/jpeg");
        }).RequireRateLimiting("public");

        var me = api.MapGroup("/me/avatar").RequireAuthorization();
        me.MapPost("", async (IFormFile? file, NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (file is null || file.Length is 0 or > MaxBytes)
                return Results.BadRequest(new ApiError("Välj en bild (högst 200 kB).", "invalidAvatar"));
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var content = buffer.ToArray();
            // Served as image/jpeg with nosniff, so anything else would not render; checked here for a clear error.
            if (content is not [0xFF, 0xD8, 0xFF, ..])
                return Results.BadRequest(new ApiError("Välj en bild (högst 200 kB).", "invalidAvatar"));
            var avatarId = Guid.NewGuid();
            return await ReplaceAsync(db, store, user.GetUserId(), avatarId,
                () => store.PutAsync(Key(avatarId), content, ct), ct);
        })
        .DisableAntiforgery()
        .RequireRateLimiting("uploads");

        me.MapDelete("", async (NpgsqlDataSource db, RawFileStore store, ClaimsPrincipal user, CancellationToken ct) =>
            await ReplaceAsync(db, store, user.GetUserId(), null, () => Task.CompletedTask, ct));
    }

    // The old file goes first, under the profile lock: if a later step fails the runner has no picture, instead of a
    // picture remaining in storage with nothing pointing to it.
    static async Task<IResult> ReplaceAsync(NpgsqlDataSource db, RawFileStore store, Guid userId, Guid? avatarId,
        Func<Task> putNew, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var current = (await conn.QueryAsync<Guid?>(new CommandDefinition(
            "select avatar_id from users where id = @userId for update", new { userId }, tx, cancellationToken: ct))).ToList();
        if (current.Count == 0) return Results.Conflict(new ApiError("Välj ett namn först.", "nameRequired"));
        if (current[0] is { } old) await store.DeleteAsync([Key(old)], ct);
        try { await putNew(); }
        catch
        {
            // The old file is already gone: drop the reference so the profile and the export don't point at it.
            await conn.ExecuteAsync("update users set avatar_id = null where id = @userId", new { userId }, tx);
            await tx.CommitAsync(CancellationToken.None);
            throw;
        }
        await conn.ExecuteAsync(new CommandDefinition("update users set avatar_id = @avatarId where id = @userId",
            new { avatarId, userId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Results.Ok(new AvatarResponse(avatarId));
    }
}

public record AvatarResponse(Guid? AvatarId);
