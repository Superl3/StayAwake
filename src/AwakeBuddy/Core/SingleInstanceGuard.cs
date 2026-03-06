using System;
using System.Threading;

namespace AwakeBuddy.Core;

public sealed class SingleInstanceGuard : IDisposable
{
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
            StartupLog.AppendLine(message);
        }
        catch
        {
        }
    }
}
