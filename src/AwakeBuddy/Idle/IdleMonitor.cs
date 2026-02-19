using System;
using System.Threading;
using Timer = System.Threading.Timer;

namespace AwakeBuddy.Idle;

public sealed class IdleMonitor : IDisposable
{
    public const int DefaultPollIntervalMilliseconds = 1000;
    public const int DefaultDebounceToleranceMilliseconds = 2000;

    private readonly object _gate = new();
    private readonly int _pollIntervalMilliseconds;
    private readonly int _debounceToleranceMilliseconds;
    private readonly SynchronizationContext? _eventContext;
    private readonly PhysicalInputTracker _physicalInputTracker;

    private int _idleThresholdMilliseconds;
    private int _idleStartBoundaryMilliseconds;
    private int _idleStopBoundaryMilliseconds;
    private Timer? _timer;
    private bool _isRunning;
    private bool _isIdle;
    private bool _isDisposed;
    private bool _ignoreInjectedInput;

    /// <summary>
    /// Events are raised on <paramref name="eventContext"/> when provided; otherwise they are raised on the timer's ThreadPool callback thread.
    /// </summary>
    public IdleMonitor(
        int idleThresholdSeconds,
        int pollIntervalMilliseconds = DefaultPollIntervalMilliseconds,
        int debounceToleranceMilliseconds = DefaultDebounceToleranceMilliseconds,
        SynchronizationContext? eventContext = null)
    {
        if (idleThresholdSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idleThresholdSeconds));
        }

        if (pollIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pollIntervalMilliseconds));
        }

        if (debounceToleranceMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceToleranceMilliseconds));
        }

        _pollIntervalMilliseconds = pollIntervalMilliseconds;
        _debounceToleranceMilliseconds = debounceToleranceMilliseconds;
        _idleThresholdMilliseconds = checked(idleThresholdSeconds * 1000);
        (_idleStartBoundaryMilliseconds, _idleStopBoundaryMilliseconds) =
            CalculateBoundaries(_idleThresholdMilliseconds, _debounceToleranceMilliseconds);
        _eventContext = eventContext;
        _physicalInputTracker = new PhysicalInputTracker();
    }

    public event EventHandler<IdleStateChangedEventArgs>? IdleStarted;

    public event EventHandler<IdleStateChangedEventArgs>? IdleStopped;

    public bool IsIdle
    {
        get
        {
            lock (_gate)
            {
                return _isIdle;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                return;
            }

            _timer ??= new Timer(OnPollTimerTick, null, Timeout.Infinite, Timeout.Infinite);
            _isRunning = true;

            if (_ignoreInjectedInput)
            {
                StartPhysicalInputTrackerWithCurrentIdleSeed();
            }

            _timer.Change(0, _pollIntervalMilliseconds);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_isDisposed || !_isRunning)
            {
                return;
            }

            _isRunning = false;
            _isIdle = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _physicalInputTracker.Stop();
        }
    }

    public bool TryGetIdleElapsedMilliseconds(out long idleElapsedMilliseconds)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_ignoreInjectedInput)
            {
                bool allowInjectedInteractionWake = _isIdle;
                if (_physicalInputTracker.TryGetIdleElapsedMilliseconds(allowInjectedInteractionWake, out idleElapsedMilliseconds))
                {
                    return true;
                }

                if (_physicalInputTracker.IsRunning && !_physicalInputTracker.HasInitializationFailed)
                {
                    idleElapsedMilliseconds = 0;
                    return false;
                }
            }
        }

        return NativeMethods.TryGetIdleElapsedMilliseconds(out idleElapsedMilliseconds);
    }

    public void UpdateIgnoreInjectedInput(bool ignoreInjectedInput)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_ignoreInjectedInput == ignoreInjectedInput)
            {
                return;
            }

            _ignoreInjectedInput = ignoreInjectedInput;

            if (!_isRunning)
            {
                return;
            }

            if (_ignoreInjectedInput)
            {
                StartPhysicalInputTrackerWithCurrentIdleSeed();
            }
            else
            {
                _physicalInputTracker.Stop();
            }
        }
    }

    private void StartPhysicalInputTrackerWithCurrentIdleSeed()
    {
        long initialIdleElapsedMilliseconds = 0;
        if (NativeMethods.TryGetIdleElapsedMilliseconds(out long nativeIdleElapsedMilliseconds))
        {
            initialIdleElapsedMilliseconds = Math.Max(0, nativeIdleElapsedMilliseconds);
        }

        _physicalInputTracker.Start(initialIdleElapsedMilliseconds);
    }

    public void UpdateIdleThresholdSeconds(int idleThresholdSeconds)
    {
        if (idleThresholdSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idleThresholdSeconds));
        }

        int idleThresholdMilliseconds = checked(idleThresholdSeconds * 1000);

        lock (_gate)
        {
            ThrowIfDisposed();

            _idleThresholdMilliseconds = idleThresholdMilliseconds;
            (_idleStartBoundaryMilliseconds, _idleStopBoundaryMilliseconds) =
                CalculateBoundaries(_idleThresholdMilliseconds, _debounceToleranceMilliseconds);
        }
    }

    private static (int StartBoundary, int StopBoundary) CalculateBoundaries(int idleThresholdMilliseconds, int debounceToleranceMilliseconds)
    {
        int effectiveDebounce = Math.Min(debounceToleranceMilliseconds, Math.Max(0, idleThresholdMilliseconds / 2));
        int startBoundary = checked(idleThresholdMilliseconds + effectiveDebounce);
        int stopBoundary = Math.Max(0, idleThresholdMilliseconds - effectiveDebounce);
        return (startBoundary, stopBoundary);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isRunning = false;
            _isIdle = false;
            _physicalInputTracker.Dispose();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnPollTimerTick(object? state)
    {
        IdleTransition transition = IdleTransition.None;
        long idleElapsedMilliseconds = 0;

        lock (_gate)
        {
            if (_isDisposed || !_isRunning)
            {
                return;
            }

            if (!TryGetIdleElapsedMilliseconds(out idleElapsedMilliseconds))
            {
                return;
            }

            if (!_isIdle && idleElapsedMilliseconds >= _idleStartBoundaryMilliseconds)
            {
                _isIdle = true;
                transition = IdleTransition.Started;
            }
            else if (_isIdle && idleElapsedMilliseconds <= _idleStopBoundaryMilliseconds)
            {
                _isIdle = false;
                transition = IdleTransition.Stopped;
            }
        }

        if (transition == IdleTransition.None)
        {
            return;
        }

        IdleStateChangedEventArgs args = new(idleElapsedMilliseconds);
        DispatchTransition(transition, args);
    }

    private void DispatchTransition(IdleTransition transition, IdleStateChangedEventArgs args)
    {
        if (_eventContext is null)
        {
            RaiseTransition(transition, args);
            return;
        }

        _eventContext.Post(
            d: static state =>
            {
                DispatchState dispatchState = (DispatchState)state!;
                dispatchState.Monitor.RaiseTransition(dispatchState.Transition, dispatchState.Args);
            },
            state: new DispatchState(this, transition, args));
    }

    private void RaiseTransition(IdleTransition transition, IdleStateChangedEventArgs args)
    {
        if (transition == IdleTransition.Started)
        {
            IdleStarted?.Invoke(this, args);
            return;
        }

        IdleStopped?.Invoke(this, args);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(IdleMonitor));
        }
    }

    private enum IdleTransition
    {
        None = 0,
        Started = 1,
        Stopped = 2
    }

    private sealed record DispatchState(IdleMonitor Monitor, IdleTransition Transition, IdleStateChangedEventArgs Args);
}

public sealed class IdleStateChangedEventArgs : EventArgs
{
    public IdleStateChangedEventArgs(long idleElapsedMilliseconds)
    {
        IdleElapsedMilliseconds = idleElapsedMilliseconds;
    }

    public long IdleElapsedMilliseconds { get; }
}
