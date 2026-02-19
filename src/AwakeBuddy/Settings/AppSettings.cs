namespace AwakeBuddy.Settings;

public enum SleepProtectionScope
{
    SystemSleepOnly = 0,
    SystemAndDisplaySleep = 1
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int IdleThresholdSeconds { get; set; } = 300;
    public double OverlayOpacity { get; set; } = 0.85;
    public bool OverlayEnabled { get; set; } = true;
    public string OverlayMonitorDeviceName { get; set; } = "";
    public bool AntiSleepEnabled { get; set; } = false;
    public int AntiSleepIntervalSeconds { get; set; } = 55;
    public SleepProtectionScope SleepProtectionScope { get; set; } = SleepProtectionScope.SystemSleepOnly;
    public bool IgnoreInjectedInputForIdle { get; set; } = false;

    public static AppSettings CreateDefault() => new();

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SchemaVersion = SchemaVersion,
            IdleThresholdSeconds = IdleThresholdSeconds,
            OverlayOpacity = OverlayOpacity,
            OverlayEnabled = OverlayEnabled,
            OverlayMonitorDeviceName = OverlayMonitorDeviceName,
            AntiSleepEnabled = AntiSleepEnabled,
            AntiSleepIntervalSeconds = AntiSleepIntervalSeconds,
            SleepProtectionScope = SleepProtectionScope,
            IgnoreInjectedInputForIdle = IgnoreInjectedInputForIdle
        };
    }
}
