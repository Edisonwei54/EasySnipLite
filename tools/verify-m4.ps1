# M4 E2E verification: scrolling long screenshot on a tall Notepad document
# Flow:
#   1. generate a 400-line text file
#   2. open Notepad with it, maximize, read its client rect (screen coords)
#   3. launch app --longcapture <x> <y> <w> <h>  (auto-copies result to clipboard)
#   4. wait for clipboard PNG; verify width ~= region width, height far beyond one screen
# NOTE: must run on an unlocked desktop (lock screen swallows synthetic input)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-m4-result.txt'
Remove-Item $log -ErrorAction SilentlyContinue
function Log($msg) { Add-Content -Path $log -Value $msg; Write-Host $msg }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinEnum {
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder t, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static string[] List(int targetPid) {
        var res = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) {
                var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                res.Add(String.Format("{0}|{1}", h.ToInt64(), cls.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return res.ToArray();
    }
    public static string ClientRect(long hwnd) {
        RECT r; GetClientRect(new IntPtr(hwnd), out r);
        POINT p = new POINT(); ClientToScreen(new IntPtr(hwnd), ref p);
        return String.Format("{0}|{1}|{2}|{3}", p.X, p.Y, r.Right, r.Bottom);
    }
}
'@

$exe = 'D:\EasySnipLite\src\EasySnipLite\bin\Debug\net10.0-windows\win-x64\EasySnipLite.exe'
$txt = 'D:\EasySnipLite\tools\verify-m4-doc.txt'
$png = 'D:\EasySnipLite\tools\verify-m4-long.png'

# --- 1. tall document (400 lines) ---
$lines = 1..400 | ForEach-Object { "Line {0} - padding padding padding padding 0123456789 abcdefghij" -f $_ }
Set-Content -Path $txt -Value $lines -Encoding ASCII
Log "OK: wrote $($lines.Count) lines to $txt"

# --- 2. open Notepad maximized ---
Remove-Item $png -ErrorAction SilentlyContinue
$np = Start-Process notepad -ArgumentList $txt -PassThru
Start-Sleep -Seconds 2
$npWin = [WinEnum]::List($np.Id) | Where-Object { $_ -match '\|Notepad$' } | Select-Object -First 1
if (-not $npWin) { Log 'FAIL: notepad window not found'; Stop-Process -Id $np.Id -Force; exit 1 }
$hwnd = [long]($npWin -split '\|')[0]
# Move to PRIMARY screen first: the secondary screen here is fully covered by Edge,
# which would swallow the wheel events and cover the capture area
[WinEnum]::MoveWindow([IntPtr]$hwnd, 0, 0, 1920, 1040, $true) | Out-Null
Start-Sleep -Milliseconds 500
[WinEnum]::ShowWindow([IntPtr]$hwnd, 3) | Out-Null   # SW_MAXIMIZE
Start-Sleep -Seconds 1
$cr = [WinEnum]::ClientRect($hwnd) -split '\|'
$x = [int]$cr[0]; $y = [int]$cr[1]; $w = [int]$cr[2]; $h = [int]$cr[3]
Log "OK: notepad client rect ${w}x${h} at $x,$y (primary screen)"

# --- 3. launch app in long-capture mode ---
$proc = Start-Process -FilePath $exe -ArgumentList "--longcapture", $x, $y, $w, $h -PassThru
Start-Sleep -Seconds 3
if ($proc.HasExited) { Log "FAIL: app exited code=$($proc.ExitCode)"; Stop-Process -Id $np.Id -Force; exit 1 }
Log "OK: app started pid=$($proc.Id) --longcapture $x $y $w $h"

# --- 4. wait for clipboard PNG (capture to bottom, auto copy) ---
$deadline = (Get-Date).AddSeconds(180)
$img = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $img = [System.Windows.Forms.Clipboard]::GetImage()
    if ($null -ne $img) {
        if ($img.Height -gt 2000) { break }          # stitched result arrived
        if ($img.Height -eq $h) { break }            # captured without scrolling - fail fast
        if ($img.Height -ne $h) { $img.Dispose(); $img = $null }  # stale image from other app? keep waiting
    }
    if ($proc.HasExited) {
        Log "FAIL: app exited before capture done code=$($proc.ExitCode)"
        Stop-Process -Id $np.Id -Force; exit 1
    }
}
if ($null -eq $img) { Log 'FAIL: timeout waiting for long image on clipboard'; Stop-Process -Id $proc.Id -Force; Stop-Process -Id $np.Id -Force; exit 1 }

$capW = $img.Width; $capH = $img.Height
Log "OK: clipboard long image ${capW}x${capH}"
$img.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$img.Dispose()

# --- 5. assertions ---
$ok = $true
if ([Math]::Abs($capW - $w) -gt 2) { Log "FAIL: width $capW vs region $w"; $ok = $false }
if ($capH -lt 2000) { Log "FAIL: height $capH < 2000 (expected 400 lines stitched)"; $ok = $false }
if ($capH -gt 12000) { Log "FAIL: height $capH > 12000 (unexpected)"; $ok = $false }

# --- 6. cleanup ---
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $np.Id -Force -ErrorAction SilentlyContinue
Remove-Item $txt -ErrorAction SilentlyContinue

if (-not $ok) { Log 'M4 verification FAILED'; exit 1 }
Log 'M4 verification PASSED'
