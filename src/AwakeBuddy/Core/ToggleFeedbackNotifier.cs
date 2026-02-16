using System;
using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AwakeBuddy.Core;

public sealed class ToggleFeedbackNotifier : IDisposable
{
    private const uint OkBeepType = 0x00000000;
    private const uint WarningBeepType = 0x00000030;

    private static readonly SolidColorBrush BackgroundBrush = new(Color.FromArgb(232, 22, 22, 24));
    private static readonly SolidColorBrush BorderBrush = new(Color.FromArgb(255, 86, 125, 193));
    private static readonly SolidColorBrush TextBrush = new(Colors.White);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _hideTimer;
    private readonly object _gate = new();

    private Window? _window;
    private TextBlock? _messageText;
    private bool _isDisposed;

    public ToggleFeedbackNotifier(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1.8)
        };
        _hideTimer.Tick += OnHideTimerTick;
    }

    public void NotifyToggle(string optionName, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(optionName))
        {
            throw new ArgumentException("Option name is required.", nameof(optionName));
        }

        string message = isEnabled
            ? $"{optionName} ON"
            : $"{optionName} OFF";

        InvokeOnDispatcher(() =>
        {
            ThrowIfDisposed();
            EnsureWindowCreated();

            _messageText!.Text = message;
            PositionWindow();
            _window!.Show();

            _hideTimer.Stop();
            _hideTimer.Start();

            if (isEnabled)
            {
                PlayToggleSound(enabled: true);
            }
            else
            {
                PlayToggleSound(enabled: false);
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
            _hideTimer.Stop();
            _hideTimer.Tick -= OnHideTimerTick;

            if (_window is not null)
            {
                _window.Close();
                _window = null;
                _messageText = null;
            }
        });
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        _window?.Hide();
    }

    private void EnsureWindowCreated()
    {
        if (_window is not null)
        {
            return;
        }

        _messageText = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(12, 8, 12, 8)
        };

        Border container = new()
        {
            Background = BackgroundBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = _messageText
        };

        _window = new Window
        {
            Width = 380,
            Height = 72,
            Topmost = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = container
        };
    }

    private void PositionWindow()
    {
        if (_window is null)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        _window.Left = workArea.Left + Math.Max(0, (workArea.Width - _window.Width) / 2);
        _window.Top = workArea.Bottom - _window.Height - 40;
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

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ToggleFeedbackNotifier));
        }
    }

    private static void PlayToggleSound(bool enabled)
    {
        try
        {
            if (enabled)
            {
                SystemSounds.Asterisk.Play();
            }
            else
            {
                SystemSounds.Exclamation.Play();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ToggleFeedbackNotifier: failed to play SystemSound: {ex}");
        }

        MessageBeep(enabled ? OkBeepType : WarningBeepType);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint uType);
}
