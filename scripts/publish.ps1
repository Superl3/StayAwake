[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('auto', 'win-x86', 'win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'auto'
)

$ErrorActionPreference = 'Stop'

function Resolve-HostRuntimeIdentifier {
    try {
        $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToUpperInvariant()
    }
    catch {
        $architecture = ($env:PROCESSOR_ARCHITECTURE + '').ToUpperInvariant()
    }

    switch ($architecture) {
        'X86' { return 'win-x86' }
        'ARM64' { return 'win-arm64' }
        default { return 'win-x64' }
    }
}

$resolvedRuntimeIdentifier = if ([string]::Equals($RuntimeIdentifier, 'auto', [System.StringComparison]::OrdinalIgnoreCase)) {
    Resolve-HostRuntimeIdentifier
}
else {
    $RuntimeIdentifier
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\AwakeBuddy\AwakeBuddy.csproj'
$outputPath = Join-Path $env:TEMP ("AwakeBuddy-publish-" + [Guid]::NewGuid().ToString("N"))
$rootExecutablePath = Join-Path $repoRoot 'AwakeBuddy.exe'
$rootPackagePath = Join-Path $repoRoot "AwakeBuddy-$resolvedRuntimeIdentifier"
$legacyRootZipPath = Join-Path $repoRoot "AwakeBuddy-$resolvedRuntimeIdentifier.zip"
$legacyDistZipPath = Join-Path $repoRoot "dist\AwakeBuddy-$resolvedRuntimeIdentifier.zip"
$distPath = Join-Path $repoRoot 'dist'
$projectBinPath = Join-Path $repoRoot 'src\AwakeBuddy\bin'
$projectObjPath = Join-Path $repoRoot 'src\AwakeBuddy\obj'

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

Write-Host "Publishing AwakeBuddy ($Configuration, $resolvedRuntimeIdentifier) to $outputPath"

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

try {
    dotnet publish $projectPath `
        -c $Configuration `
        -r $resolvedRuntimeIdentifier `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:IncludeAllContentForSelfExtract=true `
        /p:DebugType=None `
        /p:DebugSymbols=false `
        -o $outputPath

    $publishedExe = Join-Path $outputPath 'AwakeBuddy.exe'
    if (-not (Test-Path $publishedExe)) {
        throw "Published executable not found: $publishedExe"
    }

    if (Test-Path $rootExecutablePath) {
        Remove-Item -Path $rootExecutablePath -Force
    }

    if (Test-Path $legacyRootZipPath) {
        Remove-Item -Path $legacyRootZipPath -Force
    }

    if (Test-Path $legacyDistZipPath) {
        Remove-Item -Path $legacyDistZipPath -Force
    }

    if (Test-Path $rootPackagePath) {
        Remove-Item -Path $rootPackagePath -Recurse -Force
    }

    if (Test-Path $distPath) {
        Remove-Item -Path $distPath -Recurse -Force
    }

    if (Test-Path $projectBinPath) {
        Remove-Item -Path $projectBinPath -Recurse -Force
    }

    if (Test-Path $projectObjPath) {
        Remove-Item -Path $projectObjPath -Recurse -Force
    }

    Copy-Item -Path $publishedExe -Destination $rootExecutablePath -Force

    Write-Host "Publish completed: $publishedExe"
    Write-Host "Latest executable: $rootExecutablePath"
}
finally {
    if (Test-Path $outputPath) {
        Remove-Item -Path $outputPath -Recurse -Force
    }
}
