# サンプル設定でアプリを起動し、ウィンドウのスクリーンショットを保存する
param(
    [Parameter(Mandatory)] [string] $ExePath,
    [Parameter(Mandatory)] [string] $OutputPath
)
$ErrorActionPreference = 'Stop'

$settingsDirectory = Join-Path $env:APPDATA 'KidouRancher'
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'sample-projects.json') (Join-Path $settingsDirectory 'projects.json') -Force

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[Win32]::SetProcessDPIAware() | Out-Null

$process = Start-Process -FilePath $ExePath -PassThru
$deadline = (Get-Date).AddSeconds(30)
while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
}
if ($process.MainWindowHandle -eq 0) { throw 'アプリのウィンドウが表示されませんでした。' }

# URLの確認結果（最大8秒）が出そろうまで待つ
Start-Sleep -Seconds 12
[Win32]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 1

$rect = New-Object Win32+RECT
[Win32]::GetWindowRect($process.MainWindowHandle, [ref] $rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
Stop-Process -Id $process.Id
Write-Host "Saved $OutputPath ($width x $height)"
Write-Host "Window title: $($process.MainWindowTitle)"

# 起動に失敗したときの記録があれば出す（小さなウィンドウ＝エラーの画面を撮っていないかの確認用）
$startupLog = Join-Path $env:LOCALAPPDATA 'KidouRancher\startup.log'
if (Test-Path $startupLog) {
    Write-Host '--- startup.log ---'
    Get-Content $startupLog | Write-Host
}
