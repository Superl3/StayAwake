using System;
using System.Runtime.InteropServices;

namespace AwakeBuddy.Idle;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    internal static bool TryGetIdleElapsedMilliseconds(out long idleElapsedMilliseconds)
    {
        LastInputInfo inputInfo = new()
        {
            cbSize = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref inputInfo))
        {
            idleElapsedMilliseconds = 0;
            return false;
        }

        uint nowTick32 = unchecked((uint)Environment.TickCount64);
        uint elapsed32 = unchecked(nowTick32 - inputInfo.dwTime);

        idleElapsedMilliseconds = elapsed32;
        return true;
    }
}
