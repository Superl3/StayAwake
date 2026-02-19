using System;
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

    private readonly object _gate = new();
    private readonly HookProc _keyboardHookProc;
    private readonly HookProc _mouseHookProc;

    private Thread? _hookThread;
    private int _hookThreadId;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _hooksInstalled;
    private long _lastPhysicalInputTick;

    public PhysicalInputTracker()
    {
        _keyboardHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;
        _lastPhysicalInputTick = Environment.TickCount64;
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

            _lastPhysicalInputTick = Environment.TickCount64;
            _hooksInstalled = false;
            _isRunning = true;
            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "AwakeBuddy.PhysicalInputTracker"
            };
            _hookThread.Start();
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
            _ = PostThreadMessage((uint)hookThreadId, WindowMessageQuit, UIntPtr.Zero, IntPtr.Zero);
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

    public bool TryGetIdleElapsedMilliseconds(out long idleElapsedMilliseconds)
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
        idleElapsedMilliseconds = Math.Max(0, Environment.TickCount64 - lastPhysicalInputTick);
        return true;
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
        IntPtr mouseHook = SetWindowsHookEx(HookMouseLowLevel, _mouseHookProc, moduleHandle, 0);

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
                _isRunning = false;
                _hookThread = null;
                _hookThreadId = 0;
            }

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

            if (!isInjected)
            {
                Interlocked.Exchange(ref _lastPhysicalInputTick, Environment.TickCount64);
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

            if (!isInjected)
            {
                Interlocked.Exchange(ref _lastPhysicalInputTick, Environment.TickCount64);
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
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
