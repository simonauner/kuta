// Tillåt bara fasta meddelanden: rendera aldrig log state, scopes, kategorier eller undantag.
// Samma gräns gäller även om Logging-konfigurationen ändras till Trace.
public sealed class PrivateLogProvider(TextWriter output) : ILoggerProvider
{
    public const string Category = "Kuta.Diagnostics";
    public static readonly EventId Started = new(1);
    public static readonly EventId Stopped = new(2);
    public static readonly EventId RequestFailed = new(3);

    public ILogger CreateLogger(string categoryName) => new PrivateLogger(output, categoryName == Category);
    public void Dispose() { }

    sealed class PrivateLogger(TextWriter output, bool allowed) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => allowed && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = eventId.Id switch
            {
                1 => "Kuta started.",
                2 => "Kuta stopped.",
                3 => "Kuta request failed (500).",
                _ => null,
            };
            if (message is not null) output.WriteLine(message);
        }
    }
}
