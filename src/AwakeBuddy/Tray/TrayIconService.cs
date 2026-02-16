using System;
using System.Drawing;
using System.Windows.Threading;
using AwakeBuddy.Core;
using System.Windows.Forms;

namespace AwakeBuddy.Tray;

public sealed class TrayIconService : IDisposable
{
    private const string BaseTooltip = "AwakeBuddy";

    private readonly Dispatcher _dispatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _oledMenuItem;
    private readonly ToolStripMenuItem _antiSleepMenuItem;
    private readonly Action<bool> _onOledToggleChanged;
    private readonly Action<bool> _onAntiSleepToggleChanged;
    private readonly Action _onOpenSettings;
    private readonly Action _onExit;

    private bool _isDisposed;
    private bool _isUpdatingMenuState;

    public TrayIconService(
        Dispatcher dispatcher,
        bool overlayEnabled,
        bool antiSleepEnabled,
        Action<bool> onOledToggleChanged,
        Action<bool> onAntiSleepToggleChanged,
        Action onOpenSettings,
        Action onExit)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _onOledToggleChanged = onOledToggleChanged ?? throw new ArgumentNullException(nameof(onOledToggleChanged));
        _onAntiSleepToggleChanged = onAntiSleepToggleChanged ?? throw new ArgumentNullException(nameof(onAntiSleepToggleChanged));
        _onOpenSettings = onOpenSettings ?? throw new ArgumentNullException(nameof(onOpenSettings));
        _onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

        _menu = new ContextMenuStrip();

        _oledMenuItem = new ToolStripMenuItem("OLED Care Mode")
        {
            CheckOnClick = true,
            Checked = overlayEnabled
        };
        _oledMenuItem.Click += OnOledMenuItemClick;

        _antiSleepMenuItem = new ToolStripMenuItem("Anti-sleep")
        {
            CheckOnClick = true,
            Checked = antiSleepEnabled
        };
        _antiSleepMenuItem.Click += OnAntiSleepMenuItemClick;

        ToolStripMenuItem openSettingsMenuItem = new("Open settings")
        {
            CheckOnClick = false
        };
        openSettingsMenuItem.Click += OnOpenSettingsMenuItemClick;

        ToolStripMenuItem exitMenuItem = new("Exit")
        {
            CheckOnClick = false
        };
        exitMenuItem.Click += OnExitMenuItemClick;

        _menu.Items.Add(_oledMenuItem);
        _menu.Items.Add(_antiSleepMenuItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(openSettingsMenuItem);
        _menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BaseTooltip,
            Visible = true,
            ContextMenuStrip = _menu
        };
    }

    public void UpdateStatus(RuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        InvokeOnUiThread(() =>
        {
            ThrowIfDisposed();

            _isUpdatingMenuState = true;
            _oledMenuItem.Checked = status.OverlayEnabled;
            _antiSleepMenuItem.Checked = status.AntiSleepEnabled;
            _isUpdatingMenuState = false;

            _oledMenuItem.Text = status.OverlayVisible ? "OLED Care Mode (active)" : "OLED Care Mode";
            _antiSleepMenuItem.Text = status.AntiSleepActive ? "Anti-sleep (active)" : "Anti-sleep";
            _notifyIcon.Text = CreateTooltipText(status);
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _oledMenuItem.Click -= OnOledMenuItemClick;
        _antiSleepMenuItem.Click -= OnAntiSleepMenuItemClick;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void OnOledMenuItemClick(object? sender, EventArgs e)
    {
        if (_isUpdatingMenuState)
        {
            return;
        }

        _onOledToggleChanged(_oledMenuItem.Checked);
    }

    private void OnAntiSleepMenuItemClick(object? sender, EventArgs e)
    {
        if (_isUpdatingMenuState)
        {
            return;
        }

        _onAntiSleepToggleChanged(_antiSleepMenuItem.Checked);
    }

    private void OnOpenSettingsMenuItemClick(object? sender, EventArgs e)
    {
        _onOpenSettings();
    }

    private void OnExitMenuItemClick(object? sender, EventArgs e)
    {
        _onExit();
    }

    private void InvokeOnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private static string CreateTooltipText(RuntimeStatus status)
    {
        string oledText = status.OverlayEnabled
            ? (status.OverlayVisible ? "On*" : "On")
            : "Off";

        string antiSleepText = status.AntiSleepEnabled
            ? (status.AntiSleepActive ? "On*" : "On")
            : "Off";

        string tooltip = $"{BaseTooltip} O:{oledText} S:{antiSleepText}";
        return tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(TrayIconService));
        }
    }
}
