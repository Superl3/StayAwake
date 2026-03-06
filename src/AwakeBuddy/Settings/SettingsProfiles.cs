using System;

namespace AwakeBuddy.Settings;

public sealed record SettingsProfile(
    string Id,
    string Name,
    string Description,
    int IdleThresholdSeconds,
    bool OverlayEnabled,
    double OverlayOpacity,
    bool AntiSleepEnabled,
    int AntiSleepIntervalSeconds,
    SleepProtectionScope SleepProtectionScope,
    IdleInputPolicy IdleInputPolicy)
{
    public override string ToString() => Name;
}

public static class SettingsProfiles
{
    private static readonly SettingsProfile[] Profiles =
    [
        new(
            Id: "balanced",
            Name: "Balanced",
            Description: "Default-like profile with moderate overlay and anti-sleep support.",
            IdleThresholdSeconds: 300,
            OverlayEnabled: true,
            OverlayOpacity: 0.85,
            AntiSleepEnabled: true,
            AntiSleepIntervalSeconds: 55,
            SleepProtectionScope: SleepProtectionScope.SystemAndDisplaySleep,
            IdleInputPolicy: IdleInputPolicy.Native),
        new(
            Id: "oled-strict",
            Name: "OLED Strict",
            Description: "Faster OLED protection with stronger dimming and physical-only idle detection.",
            IdleThresholdSeconds: 60,
            OverlayEnabled: true,
            OverlayOpacity: 0.95,
            AntiSleepEnabled: false,
            AntiSleepIntervalSeconds: 55,
            SleepProtectionScope: SleepProtectionScope.SystemSleepOnly,
            IdleInputPolicy: IdleInputPolicy.PhysicalOnly),
        new(
            Id: "remote-work",
            Name: "Remote Work",
            Description: "Remote-control friendly mode tuned for MWB/PowerToys scenarios.",
            IdleThresholdSeconds: 480,
            OverlayEnabled: true,
            OverlayOpacity: 0.75,
            AntiSleepEnabled: true,
            AntiSleepIntervalSeconds: 45,
            SleepProtectionScope: SleepProtectionScope.SystemAndDisplaySleep,
            IdleInputPolicy: IdleInputPolicy.Hybrid)
    ];

    public static SettingsProfile[] GetProfiles() => (SettingsProfile[])Profiles.Clone();

    public static bool TryGetProfileById(string profileId, out SettingsProfile? profile)
    {
        profile = null;

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        foreach (SettingsProfile candidate in Profiles)
        {
            if (!string.Equals(candidate.Id, profileId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            profile = candidate;
            return true;
        }

        return false;
    }

    public static AppSettings ApplyProfile(AppSettings current, SettingsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(profile);

        AppSettings next = current.Clone();
        next.IdleThresholdSeconds = profile.IdleThresholdSeconds;
        next.OverlayEnabled = profile.OverlayEnabled;
        next.OverlayOpacity = profile.OverlayOpacity;
        next.AntiSleepEnabled = profile.AntiSleepEnabled;
        next.AntiSleepIntervalSeconds = profile.AntiSleepIntervalSeconds;
        next.SleepProtectionScope = profile.SleepProtectionScope;
        next.IdleInputPolicy = profile.IdleInputPolicy;
        return next;
    }
}
