# AwakeBuddy

AwakeBuddy is a lightweight Windows tray utility for OLED-friendly idle protection and keep-awake control.

It combines three goals:

- keep the PC awake when requested (system-only or system+display)
- apply a black OLED care overlay with adjustable opacity
- support single-display, multi-display, or all-display overlay targeting

## Highlights

- Tray-first app (no main window required)
- Dedicated settings window backed by `%APPDATA%\AwakeBuddy\settings.json`
- OLED Care Mode with monitor selection and `idleThresholdSeconds = 0` always-on mode
- Optional physical-input-only idle detection (`ignoreInjectedInputForIdle`) to reduce false activity from injected/remote tools
- Anti-sleep engine with configurable interval and scope
- Global hotkeys:
  - `Ctrl+Alt+O`: Toggle OLED Care Mode
  - `Ctrl+Alt+S`: Open settings
- Single-file publish workflow (`AwakeBuddy.exe`) with automatic artifact cleanup

## Screenshot

The screenshot asset is generated from a live app window and may go stale after UI changes.

To regenerate it:

```powershell
dotnet build .\src\AwakeBuddy\AwakeBuddy.csproj -c Release
.\scripts\capture-doc-screenshots.ps1
```

## Requirements

- Windows 10/11
- .NET SDK 9.0+
- Windows PowerShell 5.1+
- Git (only needed for install-from-URL flow)

## Quick Start

### Option A: Install from Git URL (interactive setup)

If you only have a repository URL, use the installer script with that URL.

From an existing local copy of this project:

```powershell
.\scripts\install-from-git.ps1 -RepoUrl "https://github.com/Superl3/StayAwake.git"
```

The installer will:

1. Clone the target repo
2. Publish a self-contained single-file executable
3. Install it to `%LOCALAPPDATA%\AwakeBuddy\bin`
4. Ask interactive initial setup questions
5. Print quick usage instructions

Useful installer options:

```powershell
.\scripts\install-from-git.ps1 -RepoUrl "<git-url>" -SkipInteractiveSetup
.\scripts\install-from-git.ps1 -RepoUrl "<git-url>" -RuntimeIdentifier win-arm64
.\scripts\install-from-git.ps1 -RepoUrl "<git-url>" -NoLaunch
```

### Option B: Build and run locally

```powershell
dotnet build .\src\AwakeBuddy\AwakeBuddy.csproj -c Release
dotnet run --project .\src\AwakeBuddy\AwakeBuddy.csproj -c Debug
```

To refresh README screenshot assets:

```powershell
dotnet build .\src\AwakeBuddy\AwakeBuddy.csproj -c Release
.\scripts\capture-doc-screenshots.ps1
```

### Option C: Shareable install command for other users

If you want another user to install directly from your repository URL:

```powershell
.\scripts\install-from-git.ps1 -RepoUrl "<your-repo-url>"
```

The setup flow now includes an idle option to ignore injected input events (useful for tools like Mouse Without Borders).

## Usage

- Run `AwakeBuddy.exe`
- Use tray icon menu to toggle:
  - `OLED Care Mode`
  - `Anti-sleep`
  - `Open settings`
  - `Exit`

When OLED or Anti-sleep toggles change, AwakeBuddy plays a short sound and shows a floating subtitle.

## Configuration

Settings are stored at `%APPDATA%\AwakeBuddy\settings.json`.

Example:

```json
{
  "schemaVersion": 1,
  "idleThresholdSeconds": 300,
  "overlayOpacity": 0.85,
  "overlayEnabled": true,
  "overlayMonitorDeviceName": "",
  "antiSleepEnabled": true,
  "antiSleepIntervalSeconds": 55,
  "sleepProtectionScope": 1,
  "ignoreInjectedInputForIdle": false
}
```

Field reference:

| Key | Type | Meaning |
| --- | --- | --- |
| `schemaVersion` | int | Settings schema version |
| `idleThresholdSeconds` | int | Idle seconds before overlay; `0` means always-on overlay |
| `overlayOpacity` | double | Overlay opacity from `0.0` to `1.0` |
| `overlayEnabled` | bool | OLED Care Mode enabled flag |
| `overlayMonitorDeviceName` | string | Target display selection: `""` primary, `"*"` all, or `"\\.\\DISPLAY1;\\.\\DISPLAY2"` |
| `antiSleepEnabled` | bool | Anti-sleep enabled flag |
| `antiSleepIntervalSeconds` | int | Keep-awake heartbeat interval in seconds |
| `sleepProtectionScope` | int | `0`: system sleep only, `1`: system + display sleep |
| `ignoreInjectedInputForIdle` | bool | When `true`, idle detection prefers physical keyboard/mouse events and ignores injected low-level input flags |

Status output is written to `%APPDATA%\AwakeBuddy\status.json`.

## Publish

```powershell
.\scripts\publish.ps1
```

Behavior:

- publishes a self-contained single-file build
- leaves only latest `AwakeBuddy.exe` in repository root
- removes previous build artifacts (`dist`, old package dirs/zips, project `bin/obj`)

Optional publish arguments:

```powershell
.\scripts\publish.ps1 -Configuration Debug
.\scripts\publish.ps1 -RuntimeIdentifier win-arm64
```

## Verification

```powershell
.\scripts\verify.ps1
```

Checks performed:

- `dotnet build` for the app project
- `powercfg /requests` snapshot (requires elevated shell)
- `%APPDATA%\AwakeBuddy\status.json` dump

## Troubleshooting

- If `powercfg /requests` does not show expected details, run PowerShell as Administrator.
- If a hotkey does not respond, verify no other app is already using the same combination.
- If a target monitor entry becomes invalid after display reconfiguration, re-open settings and reselect target displays.
- If install-from-URL fails with "Repository is empty", push project files to that repository first.
