using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AwakeBuddy.Idle;

internal sealed class PhysicalInputTracker : IDisposable
{
    private const int HookKeyboardLowLevel = 13;
    private const int HookMouseLowLevel = 14;
    private const int HookCodeAction = 0;
    private const uint KeyboardInjectedFlag = 0x00000010;
    private const uint MouseInjectedFlag = 0x00000001;
    private const uint WindowMessageQuit = 0x0012;
    private const uint MessagePeekNoRemove = 0x0000;
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageSystemKeyDown = 0x0104;
    private const uint WindowMessageLeftButtonDown = 0x0201;
    private const uint WindowMessageRightButtonDown = 0x0204;
    private const uint WindowMessageMiddleButtonDown = 0x0207;
    private const uint WindowMessageMouseWheel = 0x020A;
    private const uint WindowMessageXButtonDown = 0x020B;
    private const uint WindowMessageMouseHorizontalWheel = 0x020E;

    private readonly object _gate = new();
    private readonly HookProc _keyboardHookProc;
    private readonly HookProc _mouseHookProc;

    private Thread? _hookThread;
    private int _hookThreadId;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _hooksInstalled;
    private bool _hookInitializationFailed;
    private long _lastPhysicalInputTick;
    private long _lastInjectedInteractionTick;

    public PhysicalInputTracker()
    {
        _keyboardHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;
        _lastPhysicalInputTick = Environment.TickCount64;
    }

    public void Start(long initialIdleElapsedMilliseconds = 0)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                return;
            }

            long clampedInitialIdleElapsed = Math.Max(0, initialIdleElapsedMilliseconds);
            _lastPhysicalInputTick = Environment.TickCount64 - clampedInitialIdleElapsed;
            _lastInjectedInteractionTick = _lastPhysicalInputTick;
            _hooksInstalled = false;
            _hookInitializationFailed = false;
            _isRunning = true;
            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "AwakeBuddy.PhysicalInputTracker"
            };
            _hookThread.Start();
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _isRunning;
            }
        }
    }

    public bool HasInitializationFailed
    {
        get
        {
            lock (_gate)
            {
                return _hookInitializationFailed;
            }
        }
    }

    public void Stop()
    {
        Thread? hookThread;
        int hookThreadId;

        lock (_gate)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            hookThread = _hookThread;
            hookThreadId = _hookThreadId;
        }

        if (hookThreadId != 0)
        {
            bool postSucceeded = PostThreadMessage((uint)hookThreadId, WindowMessageQuit, UIntPtr.Zero, IntPtr.Zero);
            if (!postSucceeded)
            {
                int errorCode = Marshal.GetLastWin32Error();
                Debug.WriteLine($"PhysicalInputTracker: failed to post WM_QUIT to hook thread (error={errorCode}).");
            }
        }

        if (hookThread is not null && hookThread.IsAlive)
        {
            _ = hookThread.Join(millisecondsTimeout: 1000);
        }

        lock (_gate)
        {
            _hookThread = null;
            _hookThreadId = 0;
            _hooksInstalled = false;
        }
    }

    public bool TryGetIdleElapsedMilliseconds(bool allowInjectedInteractionWake, out long idleElapsedMilliseconds)
    {
        lock (_gate)
        {
            if (!_hooksInstalled)
            {
                idleElapsedMilliseconds = 0;
                return false;
            }
        }

        long lastPhysicalInputTick = Interlocked.Read(ref _lastPhysicalInputTick);
        long baselineInputTick = lastPhysicalInputTick;

        if (allowInjectedInteractionWake)
        {
            long lastInjectedInteractionTick = Interlocked.Read(ref _lastInjectedInteractionTick);
            if (lastInjectedInteractionTick > baselineInputTick)
            {
                baselineInputTick = lastInjectedInteractionTick;
            }
        }

        idleElapsedMilliseconds = Math.Max(0, Environment.TickCount64 - baselineInputTick);
        return true;
    }

    public bool TryGetIdleElapsedMilliseconds(out long idleElapsedMilliseconds)
    {
        return TryGetIdleElapsedMilliseconds(allowInjectedInteractionWake: false, out idleElapsedMilliseconds);
    }

    public void Dispose()
    {
        bool shouldStop;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            shouldStop = _isRunning;
        }

        if (shouldStop)
        {
            Stop();
        }
    }

    private void HookThreadMain()
    {
        NativeMessage ignoredMessage;
        _ = PeekMessage(out ignoredMessage, IntPtr.Zero, 0, 0, MessagePeekNoRemove);

        lock (_gate)
        {
            if (!_isRunning || _isDisposed)
            {
                _hookThread = null;
                _hookThreadId = 0;
                return;
            }

            _hookThreadId = GetCurrentThreadId();
        }

        IntPtr moduleHandle = GetModuleHandle(lpModuleName: null);
        IntPtr keyboardHook = SetWindowsHookEx(HookKeyboardLowLevel, _keyboardHookProc, moduleHandle, 0);
        int keyboardHookErrorCode = keyboardHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        IntPtr mouseHook = SetWindowsHookEx(HookMouseLowLevel, _mouseHookProc, moduleHandle, 0);
        int mouseHookErrorCode = mouseHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;

        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
        {
            if (keyboardHook != IntPtr.Zero)
            {
                _ = UnhookWindowsHookEx(keyboardHook);
            }

            if (mouseHook != IntPtr.Zero)
            {
                _ = UnhookWindowsHookEx(mouseHook);
            }

            lock (_gate)
            {
                _hooksInstalled = false;
                _hookInitializationFailed = true;
                _isRunning = false;
                _hookThread = null;
                _hookThreadId = 0;
            }

            Debug.WriteLine($"PhysicalInputTracker: failed to install hooks (keyboardError={keyboardHookErrorCode}, mouseError={mouseHookErrorCode}).");

            return;
        }

        lock (_gate)
        {
            _hooksInstalled = true;
        }

        try
        {
            while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        finally
        {
            _ = UnhookWindowsHookEx(keyboardHook);
            _ = UnhookWindowsHookEx(mouseHook);

            lock (_gate)
            {
                _hooksInstalled = false;
                _hookThread = null;
                _hookThreadId = 0;
            }
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HookCodeAction && lParam != IntPtr.Zero)
        {
            KbdLlHookStruct data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            bool isInjected = (data.flags & KeyboardInjectedFlag) != 0;
            long currentTick = Environment.TickCount64;

            if (!isInjected)
            {
                Interlocked.Exchange(ref _lastPhysicalInputTick, currentTick);
            }
            else if (IsInjectedKeyboardWakeMessage(wParam))
            {
                Interlocked.Exchange(ref _lastInjectedInteractionTick, currentTick);
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HookCodeAction && lParam != IntPtr.Zero)
        {
            MsLlHookStruct data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            bool isInjected = (data.flags & MouseInjectedFlag) != 0;
            long currentTick = Environment.TickCount64;

            if (!isInjected)
            {
                Interlocked.Exchange(ref _lastPhysicalInputTick, currentTick);
            }
            else if (IsInjectedMouseWakeMessage(wParam))
            {
                Interlocked.Exchange(ref _lastInjectedInteractionTick, currentTick);
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static bool IsInjectedKeyboardWakeMessage(IntPtr wParam)
    {
        uint message = unchecked((uint)wParam.ToInt64());
        return message == WindowMessageKeyDown || message == WindowMessageSystemKeyDown;
    }

    private static bool IsInjectedMouseWakeMessage(IntPtr wParam)
    {
        uint message = unchecked((uint)wParam.ToInt64());
        return message == WindowMessageLeftButtonDown ||
               message == WindowMessageRightButtonDown ||
               message == WindowMessageMiddleButtonDown ||
               message == WindowMessageXButtonDown ||
               message == WindowMessageMouseWheel ||
               message == WindowMessageMouseHorizontalWheel;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(PhysicalInputTracker));
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public NativePoint pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public NativePoint pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref NativeMessage lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref NativeMessage lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();
}
