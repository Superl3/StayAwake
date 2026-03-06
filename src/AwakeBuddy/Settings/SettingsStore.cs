using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AwakeBuddy.Core;

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
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            AppSettings sanitizedSettings = Sanitize(settings);
            string json = JsonSerializer.Serialize(sanitizedSettings, SerializerOptions);
            WriteSettingsAtomically(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFallback("settings io error writing file");
        }
    }

    public static AppSettings Sanitize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        SettingsDocument document = new()
        {
            SchemaVersion = settings.SchemaVersion,
            IdleThresholdSeconds = settings.IdleThresholdSeconds,
            OverlayOpacity = settings.OverlayOpacity,
            OverlayEnabled = settings.OverlayEnabled,
            OverlayMonitorDeviceName = settings.OverlayMonitorDeviceName,
            AntiSleepEnabled = settings.AntiSleepEnabled,
            AntiSleepIntervalSeconds = settings.AntiSleepIntervalSeconds,
            SleepProtectionScope = settings.SleepProtectionScope,
            IdleInputPolicy = settings.IdleInputPolicy,
            StartWithWindows = settings.StartWithWindows
        };

        return ResolveDefaults(document, AppSettings.CreateDefault());
    }

    public static bool TryLoadFromFile(string filePath, out AppSettings settings, out string error)
    {
        settings = AppSettings.CreateDefault();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "Import path is required.";
            return false;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            SettingsDocument? document = JsonSerializer.Deserialize<SettingsDocument>(json, SerializerOptions);

            if (document is null)
            {
                error = "The selected file is not valid AwakeBuddy settings JSON.";
                return false;
            }

            settings = ResolveDefaults(document, AppSettings.CreateDefault());
            return true;
        }
        catch (JsonException)
        {
            error = "The selected file is not valid JSON.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Unable to read settings file: {ex.Message}";
            return false;
        }
    }

    public static bool TryExportToFile(AppSettings settings, string filePath, out string error)
    {
        ArgumentNullException.ThrowIfNull(settings);

        error = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "Export path is required.";
            return false;
        }

        try
        {
            string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            AppSettings sanitizedSettings = Sanitize(settings);
            string json = JsonSerializer.Serialize(sanitizedSettings, SerializerOptions);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Unable to export settings: {ex.Message}";
            return false;
        }
    }

    private void WriteSettingsAtomically(string json)
    {
        string tempFilePath = Path.Combine(SettingsDirectoryPath, $"{SettingsFileName}.{Guid.NewGuid():N}.tmp");
        string backupFilePath = Path.Combine(SettingsDirectoryPath, $"{SettingsFileName}.{Guid.NewGuid():N}.bak");

        try
        {
            File.WriteAllText(tempFilePath, json);

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(tempFilePath, SettingsFilePath, backupFilePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFilePath, SettingsFilePath);
            }
        }
        finally
        {
            TryDeleteFile(tempFilePath);
            TryDeleteFile(backupFilePath);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"SettingsStore: cleanup failed for '{filePath}': {ex}");
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

        IdleInputPolicy idleInputPolicy = defaults.IdleInputPolicy;
        if (document.IdleInputPolicy.HasValue)
        {
            IdleInputPolicy candidate = document.IdleInputPolicy.Value;
            if (Enum.IsDefined(candidate))
            {
                idleInputPolicy = candidate;
            }
        }
        else if (document.IgnoreInjectedInputForIdle.HasValue)
        {
            idleInputPolicy = document.IgnoreInjectedInputForIdle.Value
                ? IdleInputPolicy.Hybrid
                : IdleInputPolicy.Native;
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
            IdleInputPolicy = idleInputPolicy,
            StartWithWindows = document.StartWithWindows.GetValueOrDefault(defaults.StartWithWindows)
        };
    }

    private static void LogFallback(string reason)
    {
        try
        {
            StartupLog.AppendLine($"settings fallback {reason}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsStore: failed to write startup log: {ex}");
        }
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
        public IdleInputPolicy? IdleInputPolicy { get; set; }
        public bool? IgnoreInjectedInputForIdle { get; set; }
        public bool? StartWithWindows { get; set; }
    }
}
