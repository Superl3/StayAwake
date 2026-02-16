using System.Runtime.InteropServices;
using AwakeBuddy.Settings;

namespace AwakeBuddy.Power;

[Flags]
public enum ExecutionStateFlags : uint
{
    None = 0,
    ES_SYSTEM_REQUIRED = 0x00000001,
    ES_DISPLAY_REQUIRED = 0x00000002,
    ES_CONTINUOUS = 0x80000000
}

public sealed class ExecutionStateKeepAwake
{
    public bool Enable(SleepProtectionScope sleepProtectionScope)
    {
        ExecutionStateFlags flags = ExecutionStateFlags.ES_CONTINUOUS | ExecutionStateFlags.ES_SYSTEM_REQUIRED;

        if (sleepProtectionScope == SleepProtectionScope.SystemAndDisplaySleep)
        {
            flags |= ExecutionStateFlags.ES_DISPLAY_REQUIRED;
        }

        return SetThreadExecutionState(flags) != ExecutionStateFlags.None;
    }

    public bool Refresh(SleepProtectionScope sleepProtectionScope)
    {
        ExecutionStateFlags flags = ExecutionStateFlags.ES_SYSTEM_REQUIRED;

        if (sleepProtectionScope == SleepProtectionScope.SystemAndDisplaySleep)
        {
            flags |= ExecutionStateFlags.ES_DISPLAY_REQUIRED;
        }

        return SetThreadExecutionState(flags) != ExecutionStateFlags.None;
    }

    public bool Disable()
    {
        return SetThreadExecutionState(ExecutionStateFlags.ES_CONTINUOUS) != ExecutionStateFlags.None;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionStateFlags SetThreadExecutionState(ExecutionStateFlags esFlags);
}
