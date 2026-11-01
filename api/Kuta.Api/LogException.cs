using System.Diagnostics;
using System.Runtime.CompilerServices;

// Own projection: Exception.ToString(), Message, StackTrace and Data must never be serialized.
internal sealed record LogException(string Type, LogFrame[] Frames, LogException[] Inner)
{
    // The build path comes from the compiler, not from the working directory on the production machine.
    static readonly string SourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../.."))
        + Path.DirectorySeparatorChar;

    public static LogException From(Exception exception)
    {
        var remaining = 8;
        return Read(exception, ref remaining);
    }

    static LogException Read(Exception exception, ref int remaining)
    {
        remaining--;
        try
        {
            var frames = new StackTrace(exception, true).GetFrames()
                .Take(32).Select(ReadFrame).OfType<LogFrame>().ToArray();
            var inner = new List<LogException>();
            var children = exception is AggregateException aggregate ? aggregate.InnerExceptions
                : exception.InnerException is { } child ? new[] { child }.AsEnumerable() : [];
            foreach (var childException in children)
            {
                if (remaining == 0) break;
                inner.Add(Read(childException, ref remaining));
            }
            return new(TypeName(exception.GetType()), frames, inner.ToArray());
        }
        catch (Exception)
        {
            // Diagnostic errors must not hide the original error or let its free text through.
            return new("unavailable", [], []);
        }
    }

    static LogFrame? ReadFrame(StackFrame frame)
    {
        var method = frame.GetMethod();
        if (method is null || method.Module.Assembly.IsDynamic || method is System.Reflection.Emit.DynamicMethod) return null;
        var file = frame.GetFileName();
        // Only source files under the project's build root get a path; external files are omitted.
        var relative = file?.StartsWith(SourceRoot, StringComparison.Ordinal) == true
            ? file[SourceRoot.Length..].Replace('\\', '/') : null;
        return new(method.DeclaringType is { } type ? TypeName(type) : null, method.Name,
            relative, relative is not null && frame.GetFileLineNumber() > 0 ? frame.GetFileLineNumber() : null);
    }

    static string TypeName(Type type) => type.Assembly.IsDynamic ? "dynamic"
        : (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? "unknown";

    static string SourcePath([CallerFilePath] string path = "") => path;
}

internal sealed record LogFrame(string? Type, string Method, string? File, int? Line);
