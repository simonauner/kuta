using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

public class AvatarTests(KutaFactory factory) : IClassFixture<KutaFactory>
{
    [Fact]
    public async Task Upload_replace_and_delete_serve_only_the_current_picture()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        using var owner = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var email = $"avatar-{Guid.NewGuid():N}@example.com";
        await TestSession.CreateRunnerAsync(factory, owner, conn, email, "Avatar runner", ct);
        try
        {
            using (var notJpeg = await Upload(owner, "<svg/>"u8.ToArray()))
            {
                Assert.Equal(HttpStatusCode.BadRequest, notJpeg.StatusCode);
                Assert.Contains("invalidAvatar", await notJpeg.Content.ReadAsStringAsync(ct));
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, new byte[200 * 1024 + 1])).StatusCode);

            byte[] first = [0xFF, 0xD8, 0xFF, 1];
            var firstId = (await (await Upload(owner, first)).Content.ReadFromJsonAsync<AvatarResponse>(ct))!.AvatarId!.Value;
            Assert.Equal(firstId, (await owner.GetFromJsonAsync<Me>("/api/me", ct))!.AvatarId);
            // Loaded as <img> without an account too (public profiles, feed), and cached since the id changes on upload.
            using (var image = await anonymous.GetAsync($"/api/avatars/{firstId}", ct))
            {
                Assert.Equal(HttpStatusCode.OK, image.StatusCode);
                Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
                Assert.Equal(TimeSpan.FromDays(365), image.Headers.CacheControl?.MaxAge);
                Assert.Equal(first, await image.Content.ReadAsByteArrayAsync(ct));
            }

            var secondId = (await (await Upload(owner, [0xFF, 0xD8, 0xFF, 2])).Content.ReadFromJsonAsync<AvatarResponse>(ct))!.AvatarId!.Value;
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/avatars/{firstId}", ct)).StatusCode);
            Assert.Equal(0, await Files(firstId));
            Assert.Equal(1, await Files(secondId));

            Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync("/api/me/avatar", ct)).StatusCode);
            Assert.Null((await owner.GetFromJsonAsync<Me>("/api/me", ct))!.AvatarId);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/avatars/{secondId}", ct)).StatusCode);
            Assert.Equal(0, await Files(secondId));
        }
        finally
        {
            await conn.ExecuteAsync("delete from auth.users where email = @email", new { email });
        }

        async Task<HttpResponseMessage> Upload(HttpClient client, byte[] content)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "file", "avatar.jpg");
            return await client.PostAsync("/api/me/avatar", form, ct);
        }
        Task<int> Files(Guid avatarId) => conn.ExecuteScalarAsync<int>(
            "select count(*) from storage.objects where bucket_id = 'fit-files' and name = @name",
            new { name = Avatars.Key(avatarId) });
    }
}
