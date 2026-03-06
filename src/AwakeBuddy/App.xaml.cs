using System;
using System.Reflection;
using System.Windows;
using AwakeBuddy.Core;
using AwakeBuddy.Idle;
using AwakeBuddy.Power;
using AwakeBuddy.Settings;
using AwakeBuddy.Tray;

namespace AwakeBuddy;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstanceGuard;
    private AwakeCoordinator? _awakeCoordinator;
    private SettingsStore? _settingsStore;
    private TrayIconService? _trayIconService;
    private SettingsWindow? _settingsWindow;
    private ToggleFeedbackNotifier? _toggleFeedbackNotifier;
    private GlobalHotkeyService? _globalHotkeyService;
    private bool _isFatalStartupShutdown;

    public static AppSettings CurrentSettings { get; private set; } = AppSettings.CreateDefault();

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            EnsureStartupLog();

            if (!SingleInstanceGuard.TryAcquire(out _singleInstanceGuard))
            {
                Shutdown(0);
                return;
            }

            _settingsStore = new SettingsStore();
            CurrentSettings = _settingsStore.Load();
            StartupRegistration.Apply(CurrentSettings.StartWithWindows);

            int idleThresholdSeconds = Math.Max(1, CurrentSettings.IdleThresholdSeconds);
            IdleMonitor idleMonitor = new(idleThresholdSeconds);
            AntiSleepService antiSleepService = new(CurrentSettings);
            StatusWriter statusWriter = new();

            _awakeCoordinator = new AwakeCoordinator(
                idleMonitor,
                antiSleepService,
                statusWriter,
                CurrentSettings,
                _settingsStore.SettingsFilePath,
                new WpfOledOverlay());

            _toggleFeedbackNotifier = new ToggleFeedbackNotifier(Dispatcher);

            _awakeCoordinator.StatusChanged += OnCoordinatorStatusChanged;
            _awakeCoordinator.Start();

            _trayIconService = new TrayIconService(
                Dispatcher,
                CurrentSettings.OverlayEnabled,
                CurrentSettings.AntiSleepEnabled,
                onOledToggleChanged: OnOverlayToggleChanged,
                onOledPauseRequested: OnOverlayPauseRequested,
                onOledPauseResumeRequested: OnOverlayPauseResumeRequested,
                onAntiSleepToggleChanged: OnAntiSleepToggleChanged,
                onOpenSettings: OpenSettingsWindow,
                onExit: ExitFromTray);
            _trayIconService.UpdateStatus(_awakeCoordinator.GetStatusSnapshot());

            _globalHotkeyService = new GlobalHotkeyService();
            _globalHotkeyService.ToggleOverlayRequested += OnToggleOverlayHotkeyRequested;
            _globalHotkeyService.OpenSettingsRequested += OpenSettingsWindow;

            if (HasArgument(e.Args, "--open-settings"))
            {
                Dispatcher.BeginInvoke((Action)OpenSettingsWindow);
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            StartupLog.AppendException("fatal startup exception", ex);
            DisposeRuntimeResources();
            _isFatalStartupShutdown = true;
            MessageBox.Show(
                "AwakeBuddy could not start. See logs/startup.log for details and try launching again.",
                "AwakeBuddy",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_isFatalStartupShutdown)
        {
            base.OnExit(e);
            return;
        }

        if (_awakeCoordinator is not null)
        {
            _awakeCoordinator.StatusChanged -= OnCoordinatorStatusChanged;
        }

        DisposeRuntimeResources();
        base.OnExit(e);
    }

    private void OnToggleOverlayHotkeyRequested()
    {
        ApplyTrayToggles(overlayEnabled: !CurrentSettings.OverlayEnabled, antiSleepEnabled: null);
    }

    private void OnOverlayToggleChanged(bool enabled)
    {
        ApplyTrayToggles(overlayEnabled: enabled, antiSleepEnabled: null);
    }

    private void OnAntiSleepToggleChanged(bool enabled)
    {
        ApplyTrayToggles(overlayEnabled: null, antiSleepEnabled: enabled);
    }

    private void OnOverlayPauseRequested(int durationMinutes)
    {
        if (_awakeCoordinator is null || durationMinutes <= 0)
        {
            return;
        }

        _awakeCoordinator.PauseOverlayTemporarily(TimeSpan.FromMinutes(durationMinutes));
    }

    private void OnOverlayPauseResumeRequested()
    {
        _awakeCoordinator?.ResumeOverlayNow();
    }

    private void ApplyTrayToggles(bool? overlayEnabled, bool? antiSleepEnabled)
    {
        if (_settingsStore is null || _awakeCoordinator is null)
        {
            return;
        }

        AppSettings nextSettings = CurrentSettings.Clone();

        if (overlayEnabled.HasValue)
        {
            nextSettings.OverlayEnabled = overlayEnabled.Value;
        }

        if (antiSleepEnabled.HasValue)
        {
            nextSettings.AntiSleepEnabled = antiSleepEnabled.Value;
        }

        ApplySettings(nextSettings, persistToDisk: true, notifyToggleFeedback: true);
    }

    private void OnCoordinatorStatusChanged(RuntimeStatus status)
    {
        _trayIconService?.UpdateStatus(status);
    }

    private void OpenSettingsWindow()
    {
        if (_settingsStore is null || _awakeCoordinator is null)
        {
            return;
        }

        AppSettings latestSettings = _settingsStore.Load();
        ApplySettings(latestSettings, persistToDisk: false, notifyToggleFeedback: false);

        if (_settingsWindow is not null)
        {
            _settingsWindow.LoadSettings(CurrentSettings, _settingsStore.SettingsFilePath);

            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            CurrentSettings,
            _settingsStore.SettingsFilePath,
            OnSettingsWindowSave,
            OnOverlayOpacityPreview);
        _settingsWindow.Closed += OnSettingsWindowClosed;
        _settingsWindow.Show();
    }

    private void OnSettingsWindowSave(AppSettings nextSettings)
    {
        ApplySettings(nextSettings, persistToDisk: true, notifyToggleFeedback: true);
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (_settingsStore is not null)
        {
            AppSettings persistedSettings = _settingsStore.Load();
            ApplySettings(persistedSettings, persistToDisk: false, notifyToggleFeedback: false);
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
        }

        _settingsWindow = null;
    }

    private void OnOverlayOpacityPreview(double overlayOpacity)
    {
        AppSettings previewSettings = CurrentSettings.Clone();
        previewSettings.OverlayOpacity = Math.Clamp(overlayOpacity, 0d, 1d);
        ApplySettings(previewSettings, persistToDisk: false, notifyToggleFeedback: false);
    }

    private void ApplySettings(AppSettings nextSettings, bool persistToDisk, bool notifyToggleFeedback)
    {
        if (_settingsStore is null || _awakeCoordinator is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(nextSettings);

        AppSettings previousSettings = CurrentSettings.Clone();
        CurrentSettings = nextSettings.Clone();

        if (persistToDisk)
        {
            _settingsStore.Save(CurrentSettings);
            StartupRegistration.Apply(CurrentSettings.StartWithWindows);
        }

        _awakeCoordinator.UpdateSettings(CurrentSettings);

        if (!notifyToggleFeedback)
        {
            return;
        }

        NotifyToggleFeedback("OLED Care Mode", previousSettings.OverlayEnabled, CurrentSettings.OverlayEnabled);
        NotifyToggleFeedback("Anti-sleep", previousSettings.AntiSleepEnabled, CurrentSettings.AntiSleepEnabled);
    }

    private void NotifyToggleFeedback(string optionName, bool previousValue, bool currentValue)
    {
        if (previousValue == currentValue)
        {
            return;
        }

        _toggleFeedbackNotifier?.NotifyToggle(optionName, currentValue);
    }

    private void ExitFromTray()
    {
        _trayIconService?.Dispose();
        _trayIconService = null;
        Shutdown(0);
    }

    private static void EnsureStartupLog()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        StartupLog.AppendLine($"AwakeBuddy startup version={version}");
    }

    private void DisposeRuntimeResources()
    {
        _settingsWindow?.Close();
        _settingsWindow = null;

        _trayIconService?.Dispose();
        _trayIconService = null;

        _globalHotkeyService?.Dispose();
        _globalHotkeyService = null;

        _toggleFeedbackNotifier?.Dispose();
        _toggleFeedbackNotifier = null;

        _awakeCoordinator?.Dispose();
        _awakeCoordinator = null;

        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
    }

    private static bool HasArgument(string[] args, string expected)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
