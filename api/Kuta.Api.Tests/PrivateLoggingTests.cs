using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

public class PrivateLoggingTests
{
    const string Sensitive = "runner@example.test password=secret token_hash=secret GPS=59.33,18.06 HR=180 file.fit";

    [Fact]
    public void Only_approved_metadata_is_written_including_inner_exceptions_without_reading_free_text()
    {
        using var output = new StringWriter();
        using var provider = new PrivateLogProvider(output);
        var exception = new AggregateException(Sensitive, ThrownException(), new ArgumentException(Sensitive))
        {
            Source = Sensitive,
            HelpLink = Sensitive,
        };
        exception.Data[Sensitive] = Sensitive;
        foreach (var category in new[] { PrivateLogProvider.Category, "System.Net.Http.HttpClient", "Microsoft.AspNetCore", Sensitive })
        {
            var logger = provider.CreateLogger(category);
            using var scope = logger.BeginScope(Sensitive);
            foreach (var id in new[] { 1, 2, 3, 4, 5, 999 })
                logger.Log(LogLevel.Error, new EventId(id, Sensitive), Sensitive, exception,
                    (_, _) => throw new Exception("The formatter must never be called."));
        }

        var entries = Entries(output);
        Assert.Equal(new[] { "started", "stopped", "request_failed", "startup_failed", "migration_failed" },
            entries.Select(entry => entry.GetProperty("event").GetString()));
        Assert.False(entries[0].TryGetProperty("exception", out _));
        Assert.False(entries[1].TryGetProperty("exception", out _));
        var error = entries[2].GetProperty("exception");
        Assert.Equal("System.AggregateException", error.GetProperty("type").GetString());
        var inner = error.GetProperty("inner").EnumerateArray().ToArray();
        Assert.Equal(2, inner.Length);
        Assert.Equal("System.ArgumentException", inner[1].GetProperty("type").GetString());
        var frame = Assert.Single(inner[0].GetProperty("frames").EnumerateArray(),
            frame => frame.GetProperty("method").GetString() == nameof(ThrowSensitive));
        Assert.Equal("api/Kuta.Api.Tests/PrivateLoggingTests.cs", frame.GetProperty("file").GetString());
        Assert.True(frame.GetProperty("line").GetInt32() > 0);
        AssertNoSensitiveData(output.ToString());
    }

    [Theory]
    [InlineData(500, 12.5, true, true)]
    [InlineData(99, double.NaN, false, false)]
    [InlineData(600, -1, false, false)]
    public void Only_valid_request_metrics_are_serialized(int status, double duration, bool hasStatus, bool hasDuration)
    {
        using var output = new StringWriter();
        using var provider = new PrivateLogProvider(output);
        var logger = provider.CreateLogger(PrivateLogProvider.Category);
        var metrics = new PrivateLogProvider.RequestMetrics(status, duration);
        logger.Log(LogLevel.Error, PrivateLogProvider.RequestFailed, metrics, null, (_, _) => Sensitive);
        logger.Log(LogLevel.Information, PrivateLogProvider.Started, metrics, null, (_, _) => Sensitive);
        var entries = Entries(output);
        Assert.Equal(hasStatus, entries[0].TryGetProperty("statusCode", out var actualStatus));
        Assert.Equal(hasDuration, entries[0].TryGetProperty("durationMs", out var actualDuration));
        if (hasStatus) Assert.Equal(status, actualStatus.GetInt32());
        if (hasDuration) Assert.Equal(duration, actualDuration.GetDouble());
        Assert.False(entries[1].TryGetProperty("statusCode", out _));
        Assert.False(entries[1].TryGetProperty("durationMs", out _));
    }

    [Fact]
    public void Exception_trees_and_stacks_are_bounded()
    {
        using var output = new StringWriter();
        using var provider = new PrivateLogProvider(output);
        var error = new AggregateException(Enumerable.Range(0, 20).Select(_ => ThrownException(40)));
        provider.CreateLogger(PrivateLogProvider.Category).LogError(PrivateLogProvider.RequestFailed, error, "");
        var root = Assert.Single(Entries(output)).GetProperty("exception");
        Assert.Equal(7, root.GetProperty("inner").GetArrayLength()); // Högst åtta undantag totalt.
        Assert.All(root.GetProperty("inner").EnumerateArray(),
            child => Assert.Equal(32, child.GetProperty("frames").GetArrayLength()));
        AssertNoSensitiveData(output.ToString());
    }

    [Fact]
    public void Dynamic_code_names_and_concrete_generic_arguments_are_not_written()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("synthetic-secret"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("synthetic-secret").DefineType("synthetic-secret");
        var method = type.DefineMethod("synthetic-secret", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, Sensitive);
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        var dynamicType = type.CreateType()!;
        var thrown = Assert.Throws<TargetInvocationException>(() => dynamicType.GetMethod("synthetic-secret")!.Invoke(null, null));
        var generic = (Exception)Activator.CreateInstance(typeof(GenericException<>).MakeGenericType(dynamicType))!;
        var error = new InvalidOperationException(Sensitive, new AggregateException(Sensitive, thrown, generic));
        using var output = new StringWriter();
        PrivateLogProvider.WriteFailure(output, PrivateLogProvider.MigrationFailed, error);

        var root = Assert.Single(Entries(output)).GetProperty("exception");
        var children = Assert.Single(root.GetProperty("inner").EnumerateArray()).GetProperty("inner");
        Assert.Equal(typeof(GenericException<>).FullName, children[1].GetProperty("type").GetString());
        AssertNoSensitiveData(output.ToString());
    }

    [Fact]
    public async Task Startup_errors_keep_code_metadata_but_no_sensitive_values_in_standard_output_or_error()
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
        using var entry = JsonDocument.Parse(await stderr);
        Assert.Equal("startup_failed", entry.RootElement.GetProperty("event").GetString());
        Assert.Equal("System.ArgumentException", entry.RootElement.GetProperty("exception").GetProperty("type").GetString());
        AssertNoSensitiveData(await stderr);
    }

    // HTTP verifierar att ramverkets felhantering använder samma projektion och mäter det faktiska svaret.
    [Fact]
    public async Task Unexpected_request_errors_return_neutral_json_and_log_sanitized_metadata_and_metrics()
    {
        using var output = new StringWriter();
        await using var factory = new KutaFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<SupabaseAuthClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FailingHandler());
                // Byt bara providerns utmatning så att samma produktionskod kan kontrolleras.
                services.Replace(ServiceDescriptor.Singleton<ILoggerProvider>(new PrivateLogProvider(output)));
            }));
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
        var entry = Assert.Single(Entries(output), entry => entry.GetProperty("event").GetString() == "request_failed");
        Assert.Equal("request", entry.GetProperty("operation").GetString());
        Assert.Equal(500, entry.GetProperty("statusCode").GetInt32());
        Assert.True(entry.GetProperty("durationMs").GetDouble() >= 0);
        Assert.Equal("System.InvalidOperationException", entry.GetProperty("exception").GetProperty("type").GetString());
        Assert.Contains(entry.GetProperty("exception").GetProperty("frames").EnumerateArray(),
            frame => frame.GetProperty("type").GetString() == typeof(FailingHandler).FullName);
        AssertNoSensitiveData(output.ToString());
    }

    static JsonElement[] Entries(StringWriter output) => output.ToString()
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }).ToArray();

    static void AssertNoSensitiveData(string output)
    {
        foreach (var value in new[] { "runner@example.test", "secret", "59.33", "18.06", "HR=180", "file.fit", "/Users/", "/src/" })
            Assert.DoesNotContain(value, output);
    }

    static Exception ThrownException(int depth = 0)
    {
        try { ThrowSensitive(depth); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected a synthetic failure.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowSensitive(int depth)
    {
        if (depth == 0) throw new HostileException();
        ThrowSensitive(depth - 1);
        GC.KeepAlive(depth);
    }

    sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException(Sensitive);
        public override string StackTrace => Sensitive;
        public override IDictionary Data => throw new InvalidOperationException(Sensitive);
        public override string ToString() => throw new InvalidOperationException(Sensitive);
    }

    sealed class GenericException<T> : Exception;

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException(Sensitive);
    }
}
