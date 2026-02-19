[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string[]]$RuntimeIdentifiers = @('win-x64', 'win-x86', 'win-arm64')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\AwakeBuddy\AwakeBuddy.csproj'
$assetsDirectory = Join-Path $repoRoot ("dist\release\{0}" -f $Version)
$tempRoot = Join-Path $env:TEMP ("AwakeBuddy-release-{0}-{1}" -f $Version, [Guid]::NewGuid().ToString('N'))

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

$builtAssets = New-Object System.Collections.Generic.List[string]

try {
    foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
        $publishDirectory = Join-Path $tempRoot $runtimeIdentifier
        New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

        Write-Host "Publishing $runtimeIdentifier ($Configuration)"
        dotnet publish $projectPath `
            -c $Configuration `
            -r $runtimeIdentifier `
            --self-contained true `
            /p:PublishSingleFile=true `
            /p:EnableCompressionInSingleFile=true `
            /p:IncludeNativeLibrariesForSelfExtract=true `
            /p:IncludeAllContentForSelfExtract=true `
            /p:DebugType=None `
            /p:DebugSymbols=false `
            -o $publishDirectory

        $publishedExecutable = Join-Path $publishDirectory 'AwakeBuddy.exe'
        if (-not (Test-Path $publishedExecutable)) {
            throw "Published executable not found for ${runtimeIdentifier}: $publishedExecutable"
        }

        $assetPath = Join-Path $assetsDirectory ("AwakeBuddy-{0}.exe" -f $runtimeIdentifier)
        Copy-Item -Path $publishedExecutable -Destination $assetPath -Force
        Unblock-File -Path $assetPath -ErrorAction SilentlyContinue
        $builtAssets.Add($assetPath) | Out-Null
    }

    $checksumsPath = Join-Path $assetsDirectory 'SHA256SUMS.txt'
    $checksumLines = foreach ($assetPath in $builtAssets) {
        $hash = Get-FileHash -Path $assetPath -Algorithm SHA256
        "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), ([System.IO.Path]::GetFileName($assetPath))
    }

    Set-Content -Path $checksumsPath -Value $checksumLines -Encoding UTF8
    Write-Host "Wrote checksums: $checksumsPath"

    Write-Host "Release assets ready: $assetsDirectory"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}
