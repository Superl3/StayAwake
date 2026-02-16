using System;
using System.IO;
using System.Threading;

namespace AwakeBuddy.Core;

public sealed class SingleInstanceGuard : IDisposable
{
    private const int MaxParentSearchDepth = 10;
    private const string DefaultMutexName = @"Local\AwakeBuddy";

    private readonly Mutex _mutex;
    private readonly bool _hasHandle;
    private bool _isDisposed;

    private SingleInstanceGuard(Mutex mutex, bool hasHandle)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard, string mutexName = DefaultMutexName)
    {
        Mutex mutex = new(initiallyOwned: false, name: mutexName);
        bool hasHandle;

        try
        {
            hasHandle = mutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            hasHandle = true;
        }

        if (!hasHandle)
        {
            AppendStartupLog("AwakeBuddy duplicate instance detected; exiting");
            mutex.Dispose();
            guard = null;
            return false;
        }

        guard = new SingleInstanceGuard(mutex, hasHandle);
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private static void AppendStartupLog(string message)
    {
        try
        {
            string logsPath = ResolveLogsDirectory();
            Directory.CreateDirectory(logsPath);
            string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logsPath, "startup.log"), line);
        }
        catch
        {
        }
    }

    private static string ResolveLogsDirectory()
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
}
