using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace AwakeBuddy.Core;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AwakeBuddy";
    private const string DotnetHostFileName = "dotnet.exe";

    public static void Apply(bool enabled)
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            Debug.WriteLine("StartupRegistration: process path is unavailable, skipping startup registration.");
            return;
        }

        if (string.Equals(Path.GetFileName(processPath), DotnetHostFileName, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine("StartupRegistration: dotnet.exe host detected, skipping startup registration.");
            return;
        }

        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                Debug.WriteLine("StartupRegistration: HKCU Run key unavailable.");
                return;
            }

            if (enabled)
            {
                runKey.SetValue(ValueName, QuoteCommand(processPath), RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            Debug.WriteLine($"StartupRegistration: registry update failed: {ex}");
        }
    }

    private static string QuoteCommand(string executablePath)
    {
        return $"\"{executablePath}\"";
    }
}
