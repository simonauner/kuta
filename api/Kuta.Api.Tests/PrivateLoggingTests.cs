using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public class PrivateLoggingTests
{
    const string Sensitive = "runner@example.test password=secret token_hash=secret GPS=59.33,18.06 HR=180 file.fit";

    [Fact]
    public void Only_fixed_events_are_written_even_when_state_scopes_and_exceptions_contain_sensitive_data()
    {
        using var output = new StringWriter();
        using var provider = new PrivateLogProvider(output);
        foreach (var category in new[] { PrivateLogProvider.Category, "System.Net.Http.HttpClient", "Microsoft.AspNetCore", Sensitive })
        {
            var logger = provider.CreateLogger(category);
            using var scope = logger.BeginScope(Sensitive);
            foreach (var id in new[] { PrivateLogProvider.Started, PrivateLogProvider.Stopped, PrivateLogProvider.RequestFailed, new EventId(999) })
                logger.Log(LogLevel.Error, id, Sensitive, new Exception(Sensitive),
                    (_, _) => throw new Exception("The formatter must never be called."));
        }

        Assert.Equal($"Kuta started.{Environment.NewLine}Kuta stopped.{Environment.NewLine}Kuta request failed (500).{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Startup_errors_do_not_write_exception_details_to_standard_output_or_error()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.Environment["ConnectionStrings__Default"] = Sensitive; // Ogiltig anslutningssträng, före nätverksåtkomst.
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, process.ExitCode);
        Assert.Equal("", await stdout);
        Assert.Equal($"Kuta startup failed.{Environment.NewLine}", await stderr);
    }

    // HTTP behövs för att kontrollera att ramverkets felhantering och loggproviders faktiskt ersätts.
    [Fact]
    public async Task Unexpected_request_errors_return_neutral_json_with_only_the_private_log_provider()
    {
        await using var factory = new KutaFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddHttpClient<SupabaseAuthClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new FailingHandler())));
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        await TestSession.UseCsrfToken(client, ct);
        var response = await client.PostAsJsonAsync("/api/auth/login?token_hash=synthetic-secret",
            new { email = "runner@example.test", password = "synthetic-password" }, ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(new ApiError("Ett oväntat fel inträffade.", "internalError"),
            await response.Content.ReadFromJsonAsync<ApiError>(ct));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.IsType<PrivateLogProvider>(Assert.Single(factory.Services.GetServices<ILoggerProvider>()));
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException(Sensitive);
    }
}
