# AwakeBuddy

AwakeBuddy is a lightweight Windows tray utility that keeps the system awake while you are idle, based on local settings and runtime state.

## Requirements

- Windows 10/11
- .NET SDK 9.0+
- Windows PowerShell 5.1+ (for helper scripts)

## Build

From repository root:

```powershell
dotnet build .\src\AwakeBuddy\AwakeBuddy.csproj -c Release
```

## Run (development)

```powershell
dotnet run --project .\src\AwakeBuddy\AwakeBuddy.csproj -c Debug
```

The app runs headless with a tray icon and writes runtime status to your AppData profile.

Use tray menu `Open settings` to open the dedicated settings window. The window reads from `%APPDATA%\AwakeBuddy\settings.json`, and saving applies changes to runtime + JSON.

When OLED/Anti-sleep toggles change, AwakeBuddy provides sound feedback and a short floating subtitle.

## Publish (win-x64, self-contained, single-file)

Use the script:

```powershell
.\scripts\publish.ps1
```

It publishes and leaves only the latest executable at repo root as `AwakeBuddy.exe`.

Previous publish artifacts (`dist\`, `AwakeBuddy-win-x64\`, old zip files, and build bin output) are cleaned automatically.

## Install from Git URL (interactive)

If you only have a Git URL, run:

```powershell
.\scripts\install-from-git.ps1 -RepoUrl "https://github.com/Superl3/StayAwake.git"
```

What it does:

- Clones the repo
- Publishes a self-contained single-file exe
- Installs it to `%LOCALAPPDATA%\AwakeBuddy\bin`
- Guides initial settings in interactive mode
- Prints quick usage instructions

Useful options:

```powershell
.\scripts\install-from-git.ps1 -RepoUrl "<git-url>" -SkipInteractiveSetup
.\scripts\install-from-git.ps1 -RepoUrl "<git-url>" -RuntimeIdentifier win-arm64
.\scripts\install-from-git.ps1 -RepoUrl "<git-url>" -NoLaunch
```

You can override configuration or runtime identifier:

```powershell
.\scripts\publish.ps1 -Configuration Debug
.\scripts\publish.ps1 -RuntimeIdentifier win-arm64
```

## Verify (non-interactive checks)

Use the script:

```powershell
.\scripts\verify.ps1
```

It performs:

- `dotnet build` of the app project
- `powercfg /requests` capture (when available, especially useful if `AwakeBuddy` is running)
- status dump from `%APPDATA%\AwakeBuddy\status.json`

## Settings and status locations

- Settings: `%APPDATA%\AwakeBuddy\settings.json`
- Status: `%APPDATA%\AwakeBuddy\status.json`

These files are owned by the app and updated at runtime.
