using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AwakeBuddy.Core;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyIdToggleOverlay = 1;
    private const int HotkeyIdOpenSettings = 2;

    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _isDisposed;
    private bool _toggleOverlayRegistered;
    private bool _openSettingsRegistered;

    public event Action? ToggleOverlayRequested;
    public event Action? OpenSettingsRequested;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("AwakeBuddyHotkeys")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        int vkO = KeyInterop.VirtualKeyFromKey(Key.O);
        int vkS = KeyInterop.VirtualKeyFromKey(Key.S);

        _toggleOverlayRegistered = RegisterHotKey(_source.Handle, HotkeyIdToggleOverlay, ModAlt | ModControl | ModNoRepeat, (uint)vkO);
        if (!_toggleOverlayRegistered)
        {
            int errorCode = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"GlobalHotkeyService: failed to register Ctrl+Alt+O (error={errorCode}).");
        }

        _openSettingsRegistered = RegisterHotKey(_source.Handle, HotkeyIdOpenSettings, ModAlt | ModControl | ModNoRepeat, (uint)vkS);
        if (!_openSettingsRegistered)
        {
            int errorCode = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"GlobalHotkeyService: failed to register Ctrl+Alt+S (error={errorCode}).");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_toggleOverlayRegistered)
        {
            UnregisterHotKey(_source.Handle, HotkeyIdToggleOverlay);
        }

        if (_openSettingsRegistered)
        {
            UnregisterHotKey(_source.Handle, HotkeyIdOpenSettings);
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        int id = wParam.ToInt32();

        if (id == HotkeyIdToggleOverlay)
        {
            handled = true;
            ToggleOverlayRequested?.Invoke();
            return IntPtr.Zero;
        }

        if (id == HotkeyIdOpenSettings)
        {
            handled = true;
            OpenSettingsRequested?.Invoke();
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
