using System.Text.Json;
using System.Text.Json.Serialization;

// Serialisera bara granskade fält, aldrig loggformatterare, godtyckligt state eller scopes.
public sealed class PrivateLogProvider(TextWriter output) : ILoggerProvider
{
    public const string Category = "Kuta.Diagnostics";
    public static readonly EventId Started = new(1);
    public static readonly EventId Stopped = new(2);
    public static readonly EventId RequestFailed = new(3);
    public static readonly EventId StartupFailed = new(4);
    public static readonly EventId MigrationFailed = new(5);
    public static readonly EventId JobFailed = new(6);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed record RequestMetrics(int StatusCode, double DurationMs);

    public ILogger CreateLogger(string categoryName) => new PrivateLogger(output, categoryName == Category);
    public void Dispose() { }

    // Samma integritetsgräns före ILogger finns tillgänglig vid start och migrering.
    public static void WriteFailure(TextWriter output, EventId eventId, Exception exception) =>
        new PrivateLogger(output, true).Log(LogLevel.Error, eventId, "", exception, (_, _) => "");

    sealed class PrivateLogger(TextWriter output, bool allowed) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => allowed && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var (name, operation) = eventId.Id switch
            {
                1 => ("started", "startup"),
                2 => ("stopped", "shutdown"),
                3 => ("request_failed", "request"),
                4 => ("startup_failed", "startup"),
                5 => ("migration_failed", "migration"),
                6 => ("job_failed", "job"),
                _ => (null, null),
            };
            if (name is null) return;
            var failure = eventId.Id is 3 or 4 or 5 or 6;
            var metrics = eventId == RequestFailed ? state as RequestMetrics : null;
            output.WriteLine(JsonSerializer.Serialize(new LogEntry(name, operation!,
                metrics is { StatusCode: >= 100 and <= 599 } ? metrics.StatusCode : null,
                metrics is { DurationMs: >= 0 } && double.IsFinite(metrics.DurationMs) ? metrics.DurationMs : null,
                failure && exception is not null ? LogException.From(exception) : null), Json));
        }
    }

    sealed record LogEntry(string Event, string Operation, int? StatusCode, double? DurationMs, LogException? Exception);
}
