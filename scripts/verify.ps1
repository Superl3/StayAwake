[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\AwakeBuddy\AwakeBuddy.csproj'
$statusPath = Join-Path $env:APPDATA 'AwakeBuddy\status.json'

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

Write-Host "[1/3] Building project ($Configuration)"
dotnet build $projectPath -c $Configuration

Write-Host "[2/3] powercfg /requests check"
$powerCfg = Get-Command powercfg -ErrorAction SilentlyContinue
if ($null -ne $powerCfg) {
    $isRunning = $null -ne (Get-Process -Name 'AwakeBuddy' -ErrorAction SilentlyContinue)
    if ($isRunning) {
        Write-Host 'AwakeBuddy process is running.'
    }
    else {
        Write-Host 'AwakeBuddy process is not running.'
    }

    try {
        $powerCfgOutput = cmd /c "powercfg /requests 2>&1"
        $powerCfgText = ($powerCfgOutput | Out-String)

        if ($LASTEXITCODE -ne 0 -or $powerCfgText -match 'administrator|관리자') {
            Write-Warning 'powercfg /requests requires an elevated shell on this machine. Skipping this optional check.'
        }
        else {
            Write-Host $powerCfgText
        }
    }
    catch {
        Write-Warning 'powercfg /requests failed in the current shell. Skipping this optional check.'
    }
}
else {
    Write-Warning 'powercfg is not available on this machine.'
}

Write-Host "[3/3] status.json dump"
if (Test-Path $statusPath) {
    Write-Host "Status path: $statusPath"
    Get-Content -Path $statusPath -Raw
}
else {
    Write-Warning "Status file not found: $statusPath"
}

Write-Host 'Verification checks completed.'
