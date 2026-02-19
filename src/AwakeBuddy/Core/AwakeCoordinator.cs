using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AwakeBuddy.Idle;
using AwakeBuddy.Power;
using AwakeBuddy.Settings;
using FormsScreen = System.Windows.Forms.Screen;

namespace AwakeBuddy.Core;

public interface IOledOverlay : IDisposable
{
    void Show();
    void Hide();
    void SetOpacity(double opacity);
    void SetTargetMonitor(string? monitorDeviceName);
    void SetHint(string? hintText, bool enabled);
}

public sealed class NullOledOverlay : IOledOverlay
{
    public void Show()
    {
    }

    public void Hide()
    {
    }

    public void SetOpacity(double opacity)
    {
    }

    public void SetTargetMonitor(string? monitorDeviceName)
    {
    }

    public void SetHint(string? hintText, bool enabled)
    {
    }

    public void Dispose()
    {
    }
}

public sealed class WpfOledOverlay : IOledOverlay
{
    private const string AllDisplaysToken = "*";
    private const int OverlayMaintenanceIntervalMilliseconds = 750;
    private const int HintRelocationMinMilliseconds = 12000;
    private const int HintRelocationMaxMilliseconds = 26000;
    private const int CursorOverrideDurationMilliseconds = 240;
    private const int HintMargin = 60;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private static readonly IntPtr HwndTopMost = new(-1);

    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly DispatcherTimer _overlayMaintenanceTimer;
    private readonly DispatcherTimer _hintTimer;
    private readonly DispatcherTimer _cursorOverrideTimer;
    private readonly Random _random = new();

    private readonly Dictionary<string, OverlayHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    private string _targetMonitorSelectionSpec = string.Empty;
    private string _hintText = string.Empty;
    private bool _hintEnabled;
    private bool _isDisplayEventSubscribed;
    private bool _isDisposed;
    private bool _isShown;
    private double _opacity = 1d;

    private sealed class OverlayHost
    {
        public string DeviceName { get; }
        public Window Window { get; }
        public TextBlock HintTextBlock { get; }

        public OverlayHost(string deviceName, Window window, TextBlock hintTextBlock)
        {
            DeviceName = deviceName;
            Window = window;
            HintTextBlock = hintTextBlock;
        }
    }

    public WpfOledOverlay(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _overlayMaintenanceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(OverlayMaintenanceIntervalMilliseconds)
        };
        _overlayMaintenanceTimer.Tick += OnOverlayMaintenanceTick;

        _hintTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(HintRelocationMinMilliseconds)
        };
        _hintTimer.Tick += OnHintTimerTick;

        _cursorOverrideTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(CursorOverrideDurationMilliseconds)
        };
        _cursorOverrideTimer.Tick += OnCursorOverrideTimerTick;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _isDisplayEventSubscribed = true;
    }

    public void Show()
    {
        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _isShown = true;
            RefreshHosts(shouldShow: true);
            EnsureOverlayTopmost();
            StartTimersIfNeeded();
            ActivateCursorOverride();
        });
    }

    public void Hide()
    {
        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _isShown = false;
            StopTimers();
            ClearCursorOverride();

            foreach (OverlayHost host in _hosts.Values)
            {
                host.Window.Hide();
            }
        });
    }

    public void SetOpacity(double opacity)
    {
        double clampedOpacity = Math.Clamp(opacity, 0d, 1d);

        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _opacity = clampedOpacity;
            EnsureHostsCreated();

            foreach (OverlayHost host in _hosts.Values)
            {
                host.Window.Opacity = _opacity;
            }
        });
    }

    public void SetTargetMonitor(string? monitorDeviceName)
    {
        string normalizedSpec = monitorDeviceName?.Trim() ?? string.Empty;

        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _targetMonitorSelectionSpec = normalizedSpec;
            RefreshHosts(shouldShow: _isShown);
        });
    }

    public void SetHint(string? hintText, bool enabled)
    {
        string normalizedHintText = hintText?.Trim() ?? string.Empty;

        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _hintText = normalizedHintText;
            _hintEnabled = enabled && !string.IsNullOrWhiteSpace(_hintText);

            EnsureHostsCreated();
            UpdateHintVisualState();

            if (_isShown)
            {
                StartTimersIfNeeded();
            }
        });
    }

    public void Dispose()
    {
        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isShown = false;

            StopTimers();

            _overlayMaintenanceTimer.Tick -= OnOverlayMaintenanceTick;
            _hintTimer.Tick -= OnHintTimerTick;
            _cursorOverrideTimer.Tick -= OnCursorOverrideTimerTick;

            if (_isDisplayEventSubscribed)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                _isDisplayEventSubscribed = false;
            }

            ClearCursorOverride();

            foreach (OverlayHost host in _hosts.Values)
            {
                host.Window.Close();
            }

            _hosts.Clear();
        });
    }

    private void EnsureHostsCreated()
    {
        if (_hosts.Count > 0)
        {
            return;
        }

        RefreshHosts(shouldShow: false);
    }

    private void RefreshHosts(bool shouldShow)
    {
        FormsScreen[] targets = ResolveTargetScreens();
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FormsScreen screen in targets)
        {
            wanted.Add(screen.DeviceName);

            if (!_hosts.TryGetValue(screen.DeviceName, out OverlayHost? host))
            {
                host = CreateHost(screen.DeviceName);
                _hosts.Add(screen.DeviceName, host);
            }

            UpdateHostBounds(host, screen);
            host.Window.Opacity = _opacity;
        }

        List<string>? toRemove = null;
        foreach (var kvp in _hosts)
        {
            if (wanted.Contains(kvp.Key))
            {
                continue;
            }

            toRemove ??= new List<string>();
            toRemove.Add(kvp.Key);
        }

        if (toRemove is not null)
        {
            foreach (string deviceName in toRemove)
            {
                OverlayHost host = _hosts[deviceName];
                host.Window.Close();
                _hosts.Remove(deviceName);
            }
        }

        UpdateHintVisualState();

        if (!shouldShow)
        {
            return;
        }

        foreach (OverlayHost host in _hosts.Values)
        {
            host.Window.Show();
        }
    }

    private FormsScreen[] ResolveTargetScreens()
    {
        FormsScreen[] screens = FormsScreen.AllScreens;

        if (string.Equals(_targetMonitorSelectionSpec, AllDisplaysToken, StringComparison.Ordinal))
        {
            return screens;
        }

        List<FormsScreen>? targets = null;

        if (!string.IsNullOrWhiteSpace(_targetMonitorSelectionSpec))
        {
            string[] tokens = _targetMonitorSelectionSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string token in tokens)
            {
                foreach (FormsScreen screen in screens)
                {
                    if (string.Equals(screen.DeviceName, token, StringComparison.OrdinalIgnoreCase))
                    {
                        targets ??= new List<FormsScreen>();
                        targets.Add(screen);
                        break;
                    }
                }
            }
        }

        if (targets is { Count: > 0 })
        {
            return targets.ToArray();
        }

        if (FormsScreen.PrimaryScreen is not null)
        {
            return [FormsScreen.PrimaryScreen];
        }

        if (screens.Length > 0)
        {
            return [screens[0]];
        }

        throw new InvalidOperationException("No screens were detected.");
    }

    private static OverlayHost CreateHost(string deviceName)
    {
        Window window = new()
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = true,
            ShowActivated = false,
            Cursor = Cursors.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = 1,
            Height = 1,
            Background = Brushes.Black
        };

        Canvas canvas = new()
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        TextBlock hint = new()
        {
            Text = string.Empty,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Opacity = 0.35,
            IsHitTestVisible = false
        };

        canvas.Children.Add(hint);
        window.Content = canvas;

        return new OverlayHost(deviceName, window, hint);
    }

    private void OnOverlayMaintenanceTick(object? sender, EventArgs e)
    {
        if (_isDisposed || !_isShown)
        {
            return;
        }

        UpdateHostsBounds();
        EnsureOverlayTopmost();
    }

    private void OnHintTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed || !_isShown || !_hintEnabled)
        {
            _hintTimer.Stop();
            return;
        }

        foreach (OverlayHost host in _hosts.Values)
        {
            if (!host.Window.IsVisible)
            {
                continue;
            }

            MoveHintToRandomPosition(host);
        }

        _hintTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(HintRelocationMinMilliseconds, HintRelocationMaxMilliseconds + 1));
    }

    private void OnCursorOverrideTimerTick(object? sender, EventArgs e)
    {
        _cursorOverrideTimer.Stop();
        Mouse.OverrideCursor = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        InvokeOnDispatcher(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            RefreshHosts(shouldShow: _isShown);

            if (_isShown)
            {
                EnsureOverlayTopmost();
            }
        });
    }

    private void StartTimersIfNeeded()
    {
        _overlayMaintenanceTimer.Start();

        if (_hintEnabled)
        {
            _hintTimer.Start();
        }
        else
        {
            _hintTimer.Stop();
        }
    }

    private void StopTimers()
    {
        _overlayMaintenanceTimer.Stop();
        _hintTimer.Stop();
        _cursorOverrideTimer.Stop();
    }

    private void ActivateCursorOverride()
    {
        Mouse.OverrideCursor = Cursors.None;
        _cursorOverrideTimer.Stop();
        _cursorOverrideTimer.Start();
    }

    private void ClearCursorOverride()
    {
        _cursorOverrideTimer.Stop();
        Mouse.OverrideCursor = null;
    }

    private void UpdateHostsBounds()
    {
        FormsScreen[] currentScreens = FormsScreen.AllScreens;

        foreach (OverlayHost host in _hosts.Values)
        {
            FormsScreen? match = null;
            foreach (FormsScreen screen in currentScreens)
            {
                if (string.Equals(screen.DeviceName, host.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    match = screen;
                    break;
                }
            }

            if (match is null)
            {
                continue;
            }

            UpdateHostBounds(host, match);
        }
    }

    private static void UpdateHostBounds(OverlayHost host, FormsScreen screen)
    {
        var bounds = screen.Bounds;
        Rect targetRect = ToDeviceIndependentRect(host.Window, bounds.Left, bounds.Top, bounds.Width, bounds.Height);

        if (Math.Abs(host.Window.Left - targetRect.Left) > double.Epsilon)
        {
            host.Window.Left = targetRect.Left;
        }

        if (Math.Abs(host.Window.Top - targetRect.Top) > double.Epsilon)
        {
            host.Window.Top = targetRect.Top;
        }

        if (Math.Abs(host.Window.Width - targetRect.Width) > double.Epsilon)
        {
            host.Window.Width = targetRect.Width;
        }

        if (Math.Abs(host.Window.Height - targetRect.Height) > double.Epsilon)
        {
            host.Window.Height = targetRect.Height;
        }
    }

    private static Rect ToDeviceIndependentRect(Window window, int left, int top, int width, int height)
    {
        PresentationSource? source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
        {
            return new Rect(left, top, width, height);
        }

        Matrix transform = source.CompositionTarget.TransformFromDevice;
        Point topLeft = transform.Transform(new Point(left, top));
        Point bottomRight = transform.Transform(new Point(left + width, top + height));
        return new Rect(topLeft, bottomRight);
    }

    private void UpdateHintVisualState()
    {
        foreach (OverlayHost host in _hosts.Values)
        {
            host.HintTextBlock.Text = _hintText;
            host.HintTextBlock.Visibility = _hintEnabled ? Visibility.Visible : Visibility.Hidden;

            if (_hintEnabled && host.Window.IsVisible)
            {
                MoveHintToRandomPosition(host);
            }
        }

        if (!_hintEnabled)
        {
            _hintTimer.Stop();
        }
        else if (_isShown)
        {
            _hintTimer.Start();
        }
    }

    private void MoveHintToRandomPosition(OverlayHost host)
    {
        if (string.IsNullOrWhiteSpace(_hintText))
        {
            return;
        }

        TextBlock hint = host.HintTextBlock;
        Window window = host.Window;

        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size hintSize = hint.DesiredSize;

        double windowWidth = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        double windowHeight = window.ActualHeight > 1 ? window.ActualHeight : window.Height;

        double minX = HintMargin;
        double minY = HintMargin;
        double maxX = windowWidth - hintSize.Width - HintMargin;
        double maxY = windowHeight - hintSize.Height - HintMargin;

        if (maxX < minX)
        {
            minX = 0;
            maxX = Math.Max(0, windowWidth - hintSize.Width);
        }

        if (maxY < minY)
        {
            minY = 0;
            maxY = Math.Max(0, windowHeight - hintSize.Height);
        }

        double x = maxX <= minX
            ? minX
            : minX + (_random.NextDouble() * (maxX - minX));

        double y = maxY <= minY
            ? minY
            : minY + (_random.NextDouble() * (maxY - minY));

        Canvas.SetLeft(hint, x);
        Canvas.SetTop(hint, y);
    }

    private void EnsureOverlayTopmost()
    {
        foreach (OverlayHost host in _hosts.Values)
        {
            if (!host.Window.IsVisible)
            {
                continue;
            }

            IntPtr hwnd = new WindowInteropHelper(host.Window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                continue;
            }

            host.Window.Topmost = true;
            SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    private void InvokeOnDispatcher(Action action)
    {
        lock (_gate)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.Invoke(action);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}

public sealed class AwakeCoordinator : IDisposable
{
    private const string OverlayHintText = "Ctrl+Alt+O  Toggle OLED Care Mode    Ctrl+Alt+S  Settings";
    private readonly object _gate = new();
    private readonly IdleMonitor _idleMonitor;
    private readonly AntiSleepService _antiSleepService;
    private readonly IOledOverlay _overlay;
    private readonly StatusWriter _statusWriter;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;

    private bool _isRunning;
    private bool _isDisposed;
    private bool _isIdle;
    private bool _antiSleepActive;
    private bool _overlayVisible;

    public event Action<RuntimeStatus>? StatusChanged;

    public AwakeCoordinator(
        IdleMonitor idleMonitor,
        AntiSleepService antiSleepService,
        StatusWriter statusWriter,
        AppSettings settings,
        string settingsPath,
        IOledOverlay? overlay = null)
    {
        _idleMonitor = idleMonitor ?? throw new ArgumentNullException(nameof(idleMonitor));
        _antiSleepService = antiSleepService ?? throw new ArgumentNullException(nameof(antiSleepService));
        _statusWriter = statusWriter ?? throw new ArgumentNullException(nameof(statusWriter));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        _overlay = overlay ?? new NullOledOverlay();

        _overlay.SetOpacity(_settings.OverlayOpacity);
        _overlay.SetTargetMonitor(_settings.OverlayMonitorDeviceName);
        _overlay.SetHint(OverlayHintText, _settings.OverlayEnabled && _settings.IdleThresholdSeconds == 0);
        _idleMonitor.UpdateIgnoreInjectedInput(_settings.IgnoreInjectedInputForIdle);
        _idleMonitor.IdleStarted += OnIdleStarted;
        _idleMonitor.IdleStopped += OnIdleStopped;
    }

    public void Start()
    {
        bool enableAntiSleep;
        bool showOverlay;
        RuntimeStatus status;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            enableAntiSleep = _settings.AntiSleepEnabled;
            _antiSleepActive = enableAntiSleep;
            showOverlay = _settings.OverlayEnabled && _settings.IdleThresholdSeconds == 0;
            _overlayVisible = showOverlay;
            status = CreateStatusLocked();
        }

        _antiSleepService.Start();

        if (enableAntiSleep)
        {
            _antiSleepService.Enable();
        }

        if (showOverlay)
        {
            _overlay.Show();
        }

        _idleMonitor.Start();
        PublishStatus(status);
    }

    public void Stop()
    {
        bool hideOverlay;
        bool disableAntiSleep;
        RuntimeStatus status;

        lock (_gate)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _isIdle = false;
            hideOverlay = _overlayVisible;
            disableAntiSleep = _antiSleepActive;
            _overlayVisible = false;
            _antiSleepActive = false;
            status = CreateStatusLocked();
        }

        if (hideOverlay)
        {
            _overlay.Hide();
        }

        if (disableAntiSleep)
        {
            _antiSleepService.Disable();
        }

        _idleMonitor.Stop();
        _antiSleepService.Stop();
        PublishStatus(status);
    }

    public RuntimeStatus GetStatusSnapshot()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return CreateStatusLocked();
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool setOpacity = false;
        double opacity = 0;
        bool updateIdleThreshold = false;
        bool updateIgnoreInjectedInput = false;
        int idleThresholdSeconds = 0;
        string overlayMonitorDeviceName = string.Empty;
        bool hintEnabled = false;
        bool updateAntiSleepConfiguration = false;
        int antiSleepIntervalSeconds = 0;
        SleepProtectionScope sleepProtectionScope = SleepProtectionScope.SystemSleepOnly;
        bool showOverlay = false;
        bool hideOverlay = false;
        bool enableAntiSleep = false;
        bool disableAntiSleep = false;
        RuntimeStatus status;

        lock (_gate)
        {
            ThrowIfDisposed();

            setOpacity = _settings.OverlayOpacity != settings.OverlayOpacity;
            updateIdleThreshold = _settings.IdleThresholdSeconds != settings.IdleThresholdSeconds;
            updateIgnoreInjectedInput = _settings.IgnoreInjectedInputForIdle != settings.IgnoreInjectedInputForIdle;
            updateAntiSleepConfiguration =
                _settings.AntiSleepIntervalSeconds != settings.AntiSleepIntervalSeconds ||
                _settings.SleepProtectionScope != settings.SleepProtectionScope;

            _settings.SchemaVersion = settings.SchemaVersion;
            _settings.IdleThresholdSeconds = settings.IdleThresholdSeconds;
            _settings.OverlayOpacity = settings.OverlayOpacity;
            _settings.OverlayEnabled = settings.OverlayEnabled;
            _settings.OverlayMonitorDeviceName = settings.OverlayMonitorDeviceName;
            _settings.AntiSleepEnabled = settings.AntiSleepEnabled;
            _settings.AntiSleepIntervalSeconds = settings.AntiSleepIntervalSeconds;
            _settings.SleepProtectionScope = settings.SleepProtectionScope;
            _settings.IgnoreInjectedInputForIdle = settings.IgnoreInjectedInputForIdle;

            opacity = _settings.OverlayOpacity;
            idleThresholdSeconds = _settings.IdleThresholdSeconds;
            overlayMonitorDeviceName = _settings.OverlayMonitorDeviceName;
            hintEnabled = _settings.OverlayEnabled && _settings.IdleThresholdSeconds == 0;
            antiSleepIntervalSeconds = _settings.AntiSleepIntervalSeconds;
            sleepProtectionScope = _settings.SleepProtectionScope;

            if (_isRunning)
            {
                bool nextOverlayVisible = _settings.OverlayEnabled && (_settings.IdleThresholdSeconds == 0 || _isIdle);

                if (_overlayVisible != nextOverlayVisible)
                {
                    showOverlay = nextOverlayVisible;
                    hideOverlay = !nextOverlayVisible;
                    _overlayVisible = nextOverlayVisible;
                }
            }

            if (_isRunning)
            {
                bool nextAntiSleepActive = _settings.AntiSleepEnabled;

                if (_antiSleepActive != nextAntiSleepActive)
                {
                    enableAntiSleep = nextAntiSleepActive;
                    disableAntiSleep = !nextAntiSleepActive;
                    _antiSleepActive = nextAntiSleepActive;
                }
            }

            status = CreateStatusLocked();
        }

        _overlay.SetTargetMonitor(overlayMonitorDeviceName);
        _overlay.SetHint(OverlayHintText, hintEnabled);

        if (updateIdleThreshold && idleThresholdSeconds > 0)
        {
            _idleMonitor.UpdateIdleThresholdSeconds(idleThresholdSeconds);
        }

        if (updateIgnoreInjectedInput)
        {
            _idleMonitor.UpdateIgnoreInjectedInput(_settings.IgnoreInjectedInputForIdle);
        }

        if (updateAntiSleepConfiguration)
        {
            _antiSleepService.UpdateConfiguration(antiSleepIntervalSeconds, sleepProtectionScope);
        }

        if (setOpacity)
        {
            _overlay.SetOpacity(opacity);
        }

        if (showOverlay)
        {
            _overlay.Show();
        }
        else if (hideOverlay)
        {
            _overlay.Hide();
        }

        if (enableAntiSleep)
        {
            _antiSleepService.Enable();
        }
        else if (disableAntiSleep)
        {
            _antiSleepService.Disable();
        }

        PublishStatus(status);
    }

    public void Dispose()
    {
        bool alreadyDisposed;

        lock (_gate)
        {
            alreadyDisposed = _isDisposed;
            _isDisposed = true;
        }

        if (alreadyDisposed)
        {
            return;
        }

        _idleMonitor.IdleStarted -= OnIdleStarted;
        _idleMonitor.IdleStopped -= OnIdleStopped;

        Stop();
        _overlay.Dispose();
        _idleMonitor.Dispose();
        _antiSleepService.Dispose();
    }

    private void OnIdleStarted(object? sender, IdleStateChangedEventArgs e)
    {
        HandleIdleTransition(isIdle: true);
    }

    private void OnIdleStopped(object? sender, IdleStateChangedEventArgs e)
    {
        HandleIdleTransition(isIdle: false);
    }

    private void HandleIdleTransition(bool isIdle)
    {
        bool showOverlay = false;
        bool hideOverlay = false;
        RuntimeStatus status;

        lock (_gate)
        {
            if (_isDisposed || !_isRunning || _isIdle == isIdle)
            {
                return;
            }

            _isIdle = isIdle;

            bool nextOverlayVisible = _settings.OverlayEnabled && (_settings.IdleThresholdSeconds == 0 || _isIdle);

            if (_overlayVisible != nextOverlayVisible)
            {
                showOverlay = nextOverlayVisible;
                hideOverlay = !nextOverlayVisible;
                _overlayVisible = nextOverlayVisible;
            }

            status = CreateStatusLocked();
        }

        if (showOverlay)
        {
            _overlay.Show();
        }
        else if (hideOverlay)
        {
            _overlay.Hide();
        }

        PublishStatus(status);
    }

    private void PublishStatus(RuntimeStatus status)
    {
        _statusWriter.Write(status);
        StatusChanged?.Invoke(status);
    }

    private RuntimeStatus CreateStatusLocked()
    {
        return new RuntimeStatus(
            Timestamp: DateTimeOffset.UtcNow,
            IsIdle: _isIdle,
            OverlayEnabled: _settings.OverlayEnabled,
            OverlayVisible: _overlayVisible,
            AntiSleepEnabled: _settings.AntiSleepEnabled,
            AntiSleepActive: _antiSleepActive,
            SettingsPath: _settingsPath);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AwakeCoordinator));
        }
    }
}
