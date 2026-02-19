using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AwakeBuddy.Settings;

public sealed class SettingsStore
{
    private const string AppFolderName = "AwakeBuddy";
    private const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string SettingsDirectoryPath { get; }
    public string SettingsFilePath { get; }

    public SettingsStore()
    {
        SettingsDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        SettingsFilePath = Path.Combine(SettingsDirectoryPath, SettingsFileName);
    }

    public AppSettings Load()
    {
        AppSettings defaults = AppSettings.CreateDefault();

        try
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFallback("settings io error creating directory");
            return defaults;
        }

        if (!File.Exists(SettingsFilePath))
        {
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(SettingsFilePath);
            SettingsDocument? settingsDocument = JsonSerializer.Deserialize<SettingsDocument>(json, SerializerOptions);

            if (settingsDocument is null)
            {
                LogFallback("settings parse returned null");
                Save(defaults);
                return defaults;
            }

            AppSettings resolved = ResolveDefaults(settingsDocument, defaults);
            Save(resolved);
            return resolved;
        }
        catch (JsonException)
        {
            LogFallback("settings json parse error");
            Save(defaults);
            return defaults;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFallback("settings io error reading file");
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFallback("settings io error writing file");
        }
    }

    private static AppSettings ResolveDefaults(SettingsDocument document, AppSettings defaults)
    {
        int schemaVersion = document.SchemaVersion.GetValueOrDefault(defaults.SchemaVersion);
        if (schemaVersion <= 0)
        {
            schemaVersion = defaults.SchemaVersion;
        }

        int idleThreshold = document.IdleThresholdSeconds.GetValueOrDefault(defaults.IdleThresholdSeconds);
        if (idleThreshold < 0)
        {
            idleThreshold = defaults.IdleThresholdSeconds;
        }

        double overlayOpacity = document.OverlayOpacity.GetValueOrDefault(defaults.OverlayOpacity);
        if (overlayOpacity is < 0 or > 1)
        {
            overlayOpacity = defaults.OverlayOpacity;
        }

        int antiSleepInterval = document.AntiSleepIntervalSeconds.GetValueOrDefault(defaults.AntiSleepIntervalSeconds);
        if (antiSleepInterval <= 0)
        {
            antiSleepInterval = defaults.AntiSleepIntervalSeconds;
        }

        SleepProtectionScope protectionScope = document.SleepProtectionScope.GetValueOrDefault(defaults.SleepProtectionScope);
        if (!Enum.IsDefined(protectionScope))
        {
            protectionScope = defaults.SleepProtectionScope;
        }

        string overlayMonitorDeviceName = document.OverlayMonitorDeviceName?.Trim() ?? string.Empty;

        return new AppSettings
        {
            SchemaVersion = schemaVersion,
            IdleThresholdSeconds = idleThreshold,
            OverlayOpacity = overlayOpacity,
            OverlayEnabled = document.OverlayEnabled.GetValueOrDefault(defaults.OverlayEnabled),
            OverlayMonitorDeviceName = overlayMonitorDeviceName,
            AntiSleepEnabled = document.AntiSleepEnabled.GetValueOrDefault(defaults.AntiSleepEnabled),
            AntiSleepIntervalSeconds = antiSleepInterval,
            SleepProtectionScope = protectionScope,
            IgnoreInjectedInputForIdle = document.IgnoreInjectedInputForIdle.GetValueOrDefault(defaults.IgnoreInjectedInputForIdle)
        };
    }

    private static void LogFallback(string reason)
    {
        try
        {
            string logsPath = ResolveLogsDirectory();
            Directory.CreateDirectory(logsPath);
            string message = $"{DateTimeOffset.Now:O} settings fallback {reason}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logsPath, "startup.log"), message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsStore: failed to write startup log: {ex}");
        }
    }

    private static string ResolveLogsDirectory()
    {
        const int maxParentSearchDepth = 10;
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < maxParentSearchDepth && current is not null; depth++)
        {
            string candidate = Path.Combine(current.FullName, "logs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "logs");
    }

    private sealed class SettingsDocument
    {
        public int? SchemaVersion { get; set; }
        public int? IdleThresholdSeconds { get; set; }
        public double? OverlayOpacity { get; set; }
        public bool? OverlayEnabled { get; set; }
        public string? OverlayMonitorDeviceName { get; set; }
        public bool? AntiSleepEnabled { get; set; }
        public int? AntiSleepIntervalSeconds { get; set; }
        public SleepProtectionScope? SleepProtectionScope { get; set; }
        public bool? IgnoreInjectedInputForIdle { get; set; }
    }
}
