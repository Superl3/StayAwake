using System;
using System.IO;

namespace AwakeBuddy.Core;

public static class StartupLog
{
    private const int MaxParentSearchDepth = 10;
    private const string StartupLogFileName = "startup.log";

    public static string ResolveLogsDirectory()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < MaxParentSearchDepth && current is not null; depth++)
        {
            string candidate = Path.Combine(current.FullName, "logs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "logs");
    }

    public static void AppendLine(string message)
    {
        try
        {
            string logsPath = ResolveLogsDirectory();
            Directory.CreateDirectory(logsPath);
            string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logsPath, StartupLogFileName), line);
        }
        catch
        {
        }
    }

    public static void AppendException(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string message = string.IsNullOrWhiteSpace(context)
            ? $"fatal startup error: {exception}"
            : $"{context}: {exception}";

        AppendLine(message);
    }
}
