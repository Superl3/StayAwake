[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoUrl,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'AwakeBuddy\bin'),

    [switch]$SkipInteractiveSetup,

    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)

    Write-Host "`n[AwakeBuddy] $Message"
}

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Read-YesNo {
    param(
        [string]$Prompt,
        [bool]$Default = $true
    )

    $defaultToken = if ($Default) { 'Y/n' } else { 'y/N' }

    while ($true) {
        $raw = Read-Host "$Prompt [$defaultToken]"

        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $Default
        }

        switch ($raw.Trim().ToLowerInvariant()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
            default { Write-Host 'Please enter y or n.' }
        }
    }
}

function Read-Int {
    param(
        [string]$Prompt,
        [int]$Default,
        [int]$Min,
        [int]$Max
    )

    while ($true) {
        $raw = Read-Host "$Prompt [$Default]"

        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $Default
        }

        $value = 0
        if (-not [int]::TryParse($raw, [ref]$value)) {
            Write-Host 'Please enter a valid integer.'
            continue
        }

        if ($value -lt $Min -or $value -gt $Max) {
            Write-Host "Please enter a value between $Min and $Max."
            continue
        }

        return $value
    }
}

function Read-MonitorSelectionSpec {
    Add-Type -AssemblyName System.Windows.Forms

    $screens = [System.Windows.Forms.Screen]::AllScreens
    if ($screens.Length -eq 0) {
        return ''
    }

    Write-Host ''
    Write-Host 'Select OLED target display(s):'
    Write-Host '  A) All displays'
    Write-Host '  0) Primary display'

    for ($i = 0; $i -lt $screens.Length; $i++) {
        $screen = $screens[$i]
        $primaryTag = if ($screen.Primary) { ' (Primary)' } else { '' }
        Write-Host ("  {0}) {1}{2} {3}x{4}" -f ($i + 1), $screen.DeviceName, $primaryTag, $screen.Bounds.Width, $screen.Bounds.Height)
    }

    Write-Host '  Tip: You can enter multiple numbers like 1,3'

    while ($true) {
        $raw = Read-Host 'Target [0]'

        if ([string]::IsNullOrWhiteSpace($raw) -or $raw.Trim() -eq '0') {
            return ''
        }

        if ($raw.Trim().ToUpperInvariant() -eq 'A') {
            return '*'
        }

        $tokens = $raw.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)
        if ($tokens.Length -eq 0) {
            Write-Host 'Please enter 0, A, or display numbers (for example: 1,2).'
            continue
        }

        $selected = New-Object System.Collections.Generic.List[string]
        $valid = $true

        foreach ($token in $tokens) {
            $value = 0
            if (-not [int]::TryParse($token.Trim(), [ref]$value)) {
                $valid = $false
                break
            }

            if ($value -lt 1 -or $value -gt $screens.Length) {
                $valid = $false
                break
            }

            $deviceName = $screens[$value - 1].DeviceName
            if (-not $selected.Contains($deviceName)) {
                $selected.Add($deviceName)
            }
        }

        if (-not $valid) {
            Write-Host 'Please enter valid display numbers (for example: 1,2).'
            continue
        }

        return [string]::Join(';', $selected)
    }
}

function Get-ProjectPath {
    param([string]$RepoDirectory)

    $entries = Get-ChildItem -Path $RepoDirectory -Force
    $nonGitEntries = $entries | Where-Object { $_.Name -ne '.git' }
    if ($nonGitEntries.Count -eq 0) {
        throw "Repository is empty. Push project files first, then rerun installer."
    }

    $candidate = Join-Path $RepoDirectory 'src\AwakeBuddy\AwakeBuddy.csproj'
    if (Test-Path $candidate) {
        return $candidate
    }

    $firstProject = Get-ChildItem -Path $RepoDirectory -Filter *.csproj -Recurse -File | Select-Object -First 1
    if ($null -eq $firstProject) {
        throw 'No .csproj file found in cloned repository.'
    }

    return $firstProject.FullName
}

function Write-InteractiveSettings {
    param([string]$SettingsPath)

    Write-Step 'Interactive initial setup'

    $overlayEnabled = Read-YesNo -Prompt 'Enable OLED Care Mode' -Default $true
    $idleThreshold = Read-Int -Prompt 'Idle threshold seconds (0 = always-on overlay)' -Default 300 -Min 0 -Max 86400
    $overlayOpacityPercent = Read-Int -Prompt 'OLED overlay opacity percent (0-100)' -Default 85 -Min 0 -Max 100
    $monitorSelection = Read-MonitorSelectionSpec

    $antiSleepEnabled = Read-YesNo -Prompt 'Enable Anti-sleep' -Default $true
    $antiSleepInterval = Read-Int -Prompt 'Anti-sleep interval seconds' -Default 55 -Min 1 -Max 3600
    $ignoreInjectedInputForIdle = Read-YesNo -Prompt 'Ignore injected input for idle detection (recommended with Mouse Without Borders)' -Default $false

    Write-Host ''
    Write-Host 'Sleep protection scope:'
    Write-Host '  0) System sleep only'
    Write-Host '  1) System + display sleep (Both)'
    $sleepScope = Read-Int -Prompt 'Scope' -Default 1 -Min 0 -Max 1

    $settings = [ordered]@{
        schemaVersion = 1
        idleThresholdSeconds = $idleThreshold
        overlayOpacity = [Math]::Round(($overlayOpacityPercent / 100.0), 2)
        overlayEnabled = $overlayEnabled
        overlayMonitorDeviceName = $monitorSelection
        antiSleepEnabled = $antiSleepEnabled
        antiSleepIntervalSeconds = $antiSleepInterval
        sleepProtectionScope = $sleepScope
        ignoreInjectedInputForIdle = $ignoreInjectedInputForIdle
    }

    $settingsDirectory = Split-Path -Parent $SettingsPath
    New-Item -Path $settingsDirectory -ItemType Directory -Force | Out-Null
    $settings | ConvertTo-Json -Depth 5 | Set-Content -Path $SettingsPath -Encoding UTF8

    Write-Host "Saved settings: $SettingsPath"
}

Require-Command -Name 'git'
Require-Command -Name 'dotnet'

$tempRoot = Join-Path $env:TEMP ("AwakeBuddy-install-" + [Guid]::NewGuid().ToString('N'))
$cloneDirectory = Join-Path $tempRoot 'repo'
$publishDirectory = Join-Path $tempRoot 'publish'
$settingsPath = Join-Path (Join-Path $env:APPDATA 'AwakeBuddy') 'settings.json'

try {
    New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

    Write-Step "Cloning repository: $RepoUrl"
    git clone --depth 1 "$RepoUrl" "$cloneDirectory"

    $projectPath = Get-ProjectPath -RepoDirectory $cloneDirectory

    Write-Step "Publishing self-contained executable ($RuntimeIdentifier, $Configuration)"
    New-Item -Path $publishDirectory -ItemType Directory -Force | Out-Null

    dotnet publish "$projectPath" `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:IncludeAllContentForSelfExtract=true `
        /p:DebugType=None `
        /p:DebugSymbols=false `
        -o "$publishDirectory"

    $publishedExe = Get-ChildItem -Path $publishDirectory -Filter *.exe -File | Select-Object -First 1
    if ($null -eq $publishedExe) {
        throw "No executable found in publish output: $publishDirectory"
    }

    if (Test-Path $InstallDirectory) {
        Remove-Item -Path $InstallDirectory -Recurse -Force
    }

    New-Item -Path $InstallDirectory -ItemType Directory -Force | Out-Null
    $installedExePath = Join-Path $InstallDirectory $publishedExe.Name
    Copy-Item -Path $publishedExe.FullName -Destination $installedExePath -Force

    if (-not $SkipInteractiveSetup) {
        Write-InteractiveSettings -SettingsPath $settingsPath
    }

    Write-Step 'Install completed'
    Write-Host "Executable: $installedExePath"
    Write-Host "Settings:   $settingsPath"
    Write-Host ''
    Write-Host 'Quick usage:'
    Write-Host "  1) Run: & '$installedExePath'"
    Write-Host '  2) Open settings: tray icon -> Open settings, or Ctrl+Alt+S'
    Write-Host '  3) Toggle OLED Care Mode: Ctrl+Alt+O'

    if (-not $NoLaunch) {
        $launchNow = Read-YesNo -Prompt 'Launch AwakeBuddy now' -Default $true
        if ($launchNow) {
            Start-Process -FilePath $installedExePath | Out-Null
            Write-Host 'AwakeBuddy started.'
        }
    }
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}
