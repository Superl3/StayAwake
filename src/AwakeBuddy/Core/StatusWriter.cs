using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AwakeBuddy.Core;

public sealed record RuntimeStatus(
    DateTimeOffset Timestamp,
    bool IsIdle,
    bool OverlayEnabled,
    bool OverlayVisible,
    bool AntiSleepEnabled,
    bool AntiSleepActive,
    string SettingsPath);

public sealed class StatusWriter
{
    private const string AppFolderName = "AwakeBuddy";
    private const string StatusFileName = "status.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _writeGate = new();

    public string StatusDirectoryPath { get; }
    public string StatusFilePath { get; }

    public StatusWriter()
    {
        StatusDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        StatusFilePath = Path.Combine(StatusDirectoryPath, StatusFileName);
    }

    public void Write(RuntimeStatus status)
    {
        lock (_writeGate)
        {
            try
            {
                Directory.CreateDirectory(StatusDirectoryPath);
                string json = JsonSerializer.Serialize(status, SerializerOptions);
                File.WriteAllText(StatusFilePath, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"StatusWriter: failed to write status: {ex}");
            }
        }
    }
}
