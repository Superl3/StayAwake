[CmdletBinding()]
param(
    [string]$ExecutablePath = (Join-Path (Get-Location) 'src\AwakeBuddy\bin\Release\net9.0-windows\AwakeBuddy.exe'),
    [string]$OutputDirectory = (Join-Path (Get-Location) 'docs\images')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ExecutablePath)) {
    throw "AwakeBuddy executable not found: $ExecutablePath"
}

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NativeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
"@

function Stop-AwakeBuddy {
    Get-Process -Name AwakeBuddy -ErrorAction SilentlyContinue | Stop-Process -Force
}

New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null

Stop-AwakeBuddy
$process = Start-Process -FilePath $ExecutablePath -ArgumentList '--open-settings' -PassThru

try {
    $hwnd = [IntPtr]::Zero

    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        $hwnd = $process.MainWindowHandle
        if ($hwnd -ne [IntPtr]::Zero) {
            break
        }
    }

    if ($hwnd -eq [IntPtr]::Zero) {
        throw 'Could not find AwakeBuddy settings window handle.'
    }

    $rect = New-Object NativeWindowCapture+RECT
    $ok = [NativeWindowCapture]::GetWindowRect($hwnd, [ref]$rect)
    if (-not $ok) {
        throw 'GetWindowRect failed.'
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $outputPath = Join-Path $OutputDirectory 'settings-window.png'
        $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Saved screenshot: $outputPath"
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }

    Stop-AwakeBuddy
}
