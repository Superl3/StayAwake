using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AwakeBuddy.Core;
using System.Windows.Forms;

namespace AwakeBuddy.Tray;

public sealed class TrayIconService : IDisposable
{
    private const string BaseTooltip = "AwakeBuddy";

    private readonly Dispatcher _dispatcher;
    private readonly Icon _trayIcon;
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

        _trayIcon = CreateMonitorLikeIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
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
        _trayIcon.Dispose();
        _menu.Dispose();
    }

    private static Icon CreateMonitorLikeIcon()
    {
        try
        {
            const int sourceSize = 512;
            const int iconSize = 32;

            using Bitmap sourceBitmap = new(sourceSize, sourceSize);
            using Graphics sourceGraphics = Graphics.FromImage(sourceBitmap);

            sourceGraphics.Clear(Color.Transparent);
            sourceGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            sourceGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            sourceGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            sourceGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

            using Pen bezelPen = new(Color.FromArgb(245, 245, 245), 30f)
            {
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };

            using Pen screenPen = new(Color.FromArgb(225, 225, 225), 14f)
            {
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };

            using SolidBrush boltBrush = new(Color.FromArgb(255, 245, 201, 55));
            using Pen boltPen = new(Color.FromArgb(255, 255, 224, 130), 8f)
            {
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };

            sourceGraphics.DrawRectangle(bezelPen, 52f, 36f, 408f, 304f);
            sourceGraphics.DrawRectangle(screenPen, 84f, 68f, 344f, 240f);

            PointF[] bolt =
            [
                new PointF(286f, 92f),
                new PointF(228f, 206f),
                new PointF(272f, 206f),
                new PointF(232f, 300f),
                new PointF(322f, 174f),
                new PointF(276f, 174f)
            ];

            sourceGraphics.FillPolygon(boltBrush, bolt);
            sourceGraphics.DrawPolygon(boltPen, bolt);

            sourceGraphics.DrawLine(bezelPen, 256f, 340f, 256f, 418f);
            sourceGraphics.DrawLine(bezelPen, 172f, 446f, 340f, 446f);

            using Bitmap iconBitmap = new(iconSize, iconSize);
            using Graphics iconGraphics = Graphics.FromImage(iconBitmap);

            iconGraphics.Clear(Color.Transparent);
            iconGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            iconGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            iconGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            iconGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            iconGraphics.DrawImage(
                sourceBitmap,
                new Rectangle(0, 0, iconSize, iconSize),
                new Rectangle(0, 0, sourceSize, sourceSize),
                GraphicsUnit.Pixel);

            IntPtr iconHandle = iconBitmap.GetHicon();

            try
            {
                using Icon temp = Icon.FromHandle(iconHandle);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrayIconService: monitor icon creation failed: {ex}");
            return (Icon)SystemIcons.Application.Clone();
        }
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
