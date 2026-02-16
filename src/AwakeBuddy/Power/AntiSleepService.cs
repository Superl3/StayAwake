using System;
using System.Threading;
using AwakeBuddy.Settings;
using Timer = System.Threading.Timer;

namespace AwakeBuddy.Power;

public sealed class AntiSleepService : IDisposable
{
    private readonly object _gate = new();
    private readonly ExecutionStateKeepAwake _executionStateKeepAwake;
    private readonly InputKeepAlive _inputKeepAlive;

    private int _heartbeatIntervalMilliseconds;
    private SleepProtectionScope _sleepProtectionScope;
    private Timer? _heartbeatTimer;
    private bool _preventSleepRequest = true;
    private bool _inputKeepalive;
    private bool _isStarted;
    private bool _isEnabled;
    private bool _isDisposed;

    public AntiSleepService(
        AppSettings settings,
        ExecutionStateKeepAwake? executionStateKeepAwake = null,
        InputKeepAlive? inputKeepAlive = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int heartbeatIntervalSeconds = settings.AntiSleepIntervalSeconds > 0
            ? settings.AntiSleepIntervalSeconds
            : AppSettings.CreateDefault().AntiSleepIntervalSeconds;

        _heartbeatIntervalMilliseconds = checked(heartbeatIntervalSeconds * 1000);
        _sleepProtectionScope = Enum.IsDefined(settings.SleepProtectionScope)
            ? settings.SleepProtectionScope
            : SleepProtectionScope.SystemSleepOnly;

        _executionStateKeepAwake = executionStateKeepAwake ?? new ExecutionStateKeepAwake();
        _inputKeepAlive = inputKeepAlive ?? new InputKeepAlive();
    }

    public bool PreventSleepRequest
    {
        get
        {
            lock (_gate)
            {
                return _preventSleepRequest;
            }
        }
        set
        {
            bool shouldClearRequest = false;
            bool shouldApplyHeartbeat = false;

            lock (_gate)
            {
                ThrowIfDisposed();

                if (_preventSleepRequest == value)
                {
                    return;
                }

                _preventSleepRequest = value;

                if (_isEnabled)
                {
                    shouldApplyHeartbeat = value;
                    shouldClearRequest = !value;
                }
            }

            if (shouldClearRequest)
            {
                _executionStateKeepAwake.Disable();
            }

            if (shouldApplyHeartbeat)
            {
                ApplyHeartbeat(includeContinuousExecutionState: true);
            }
        }
    }

    public bool InputKeepalive
    {
        get
        {
            lock (_gate)
            {
                return _inputKeepalive;
            }
        }
        set
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _inputKeepalive = value;
            }
        }
    }

    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _isStarted;
            }
        }
    }

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _isEnabled;
            }
        }
    }

    public void UpdateConfiguration(int heartbeatIntervalSeconds, SleepProtectionScope sleepProtectionScope)
    {
        if (heartbeatIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatIntervalSeconds));
        }

        if (!Enum.IsDefined(sleepProtectionScope))
        {
            throw new ArgumentOutOfRangeException(nameof(sleepProtectionScope));
        }

        int heartbeatIntervalMilliseconds = checked(heartbeatIntervalSeconds * 1000);
        bool shouldApplyHeartbeat = false;

        lock (_gate)
        {
            ThrowIfDisposed();

            _heartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds;
            _sleepProtectionScope = sleepProtectionScope;

            if (_isStarted && _isEnabled && _heartbeatTimer is not null)
            {
                _heartbeatTimer.Change(_heartbeatIntervalMilliseconds, _heartbeatIntervalMilliseconds);
                shouldApplyHeartbeat = true;
            }
        }

        if (shouldApplyHeartbeat)
        {
            ApplyHeartbeat(includeContinuousExecutionState: true);
        }
    }

    public void Start()
    {
        bool shouldApplyInitialHeartbeat = false;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_isStarted)
            {
                return;
            }

            _heartbeatTimer ??= new Timer(OnHeartbeatTimerTick, null, Timeout.Infinite, Timeout.Infinite);
            _isStarted = true;

            if (!_isEnabled)
            {
                return;
            }

            _heartbeatTimer.Change(_heartbeatIntervalMilliseconds, _heartbeatIntervalMilliseconds);
            shouldApplyInitialHeartbeat = true;
        }

        if (shouldApplyInitialHeartbeat)
        {
            ApplyHeartbeat(includeContinuousExecutionState: true);
        }
    }

    public void Stop()
    {
        bool shouldClearRequest = false;

        lock (_gate)
        {
            if (_isDisposed || !_isStarted)
            {
                return;
            }

            _isStarted = false;
            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            shouldClearRequest = _isEnabled;
        }

        if (shouldClearRequest)
        {
            _executionStateKeepAwake.Disable();
        }
    }

    public void Enable()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_isEnabled)
            {
                return;
            }

            _isEnabled = true;

            if (!_isStarted)
            {
                _heartbeatTimer ??= new Timer(OnHeartbeatTimerTick, null, Timeout.Infinite, Timeout.Infinite);
                _isStarted = true;
            }

            _heartbeatTimer!.Change(_heartbeatIntervalMilliseconds, _heartbeatIntervalMilliseconds);
        }

        ApplyHeartbeat(includeContinuousExecutionState: true);
    }

    public void Disable()
    {
        bool shouldClearRequest = false;

        lock (_gate)
        {
            if (_isDisposed || !_isEnabled)
            {
                return;
            }

            _isEnabled = false;
            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            shouldClearRequest = true;
        }

        if (shouldClearRequest)
        {
            _executionStateKeepAwake.Disable();
        }
    }

    public void Dispose()
    {
        Timer? timerToDispose;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isStarted = false;
            _isEnabled = false;

            timerToDispose = _heartbeatTimer;
            _heartbeatTimer = null;
        }

        timerToDispose?.Dispose();
        _executionStateKeepAwake.Disable();
    }

    private void OnHeartbeatTimerTick(object? state)
    {
        ApplyHeartbeat(includeContinuousExecutionState: false);
    }

    private void ApplyHeartbeat(bool includeContinuousExecutionState)
    {
        lock (_gate)
        {
            if (_isDisposed || !_isStarted || !_isEnabled)
            {
                return;
            }

            if (_preventSleepRequest)
            {
                if (includeContinuousExecutionState)
                {
                    _executionStateKeepAwake.Enable(_sleepProtectionScope);
                }
                else
                {
                    _executionStateKeepAwake.Refresh(_sleepProtectionScope);
                }
            }

            if (_inputKeepalive)
            {
                _inputKeepAlive.Pulse();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AntiSleepService));
        }
    }
}
