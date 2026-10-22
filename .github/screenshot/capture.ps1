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
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    public static System.Collections.Generic.List<IntPtr> VisibleWindowsOf(int pid) {
        var result = new System.Collections.Generic.List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) result.Add(h); return true; }, IntPtr.Zero);
        return result;
    }
    public static string TitleOf(IntPtr hWnd) { var b = new System.Text.StringBuilder(256); GetWindowText(hWnd, b, 256); return b.ToString(); }
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

# プロセスの見えているウィンドウをすべて出し（エラー画面などの確認用）、メインの画面を撮る
$target = [IntPtr]::Zero
$largest = 0
foreach ($window in [Win32]::VisibleWindowsOf($process.Id)) {
    $r = New-Object Win32+RECT
    [Win32]::GetWindowRect($window, [ref] $r) | Out-Null
    $title = [Win32]::TitleOf($window)
    $area = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
    Write-Host ("Window: '{0}' {1}x{2}" -f $title, ($r.Right - $r.Left), ($r.Bottom - $r.Top))
    if ($title -eq '起動ランチャー') { $target = $window; $largest = [int]::MaxValue }
    elseif ($area -gt $largest) { $target = $window; $largest = $area }
}
if ($target -eq [IntPtr]::Zero) { $target = $process.MainWindowHandle }
[Win32]::SetForegroundWindow($target) | Out-Null
Start-Sleep -Seconds 1

$rect = New-Object Win32+RECT
[Win32]::GetWindowRect($target, [ref] $rect) | Out-Null
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
Write-Host "Captured: '$([Win32]::TitleOf($target))'"

# 起動に失敗したときの記録があれば出す（小さなウィンドウ＝エラーの画面を撮っていないかの確認用）
$startupLog = Join-Path $env:LOCALAPPDATA 'KidouRancher\startup.log'
if (Test-Path $startupLog) {
    Write-Host '--- startup.log ---'
    Get-Content $startupLog | Write-Host
}
