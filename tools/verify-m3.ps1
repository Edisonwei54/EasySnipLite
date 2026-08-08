# M3 E2E verification: hotkey -> select -> Enter opens editor -> draw -> undo -> copy
# Scenarios:
#   A. capture, drag 300x200, Enter -> editor window opens (non-fullscreen owned window)
#   B. click Rect tool, drag rect on canvas, Ctrl+C -> clipboard P1 (annotated)
#      Ctrl+Z (undo), Ctrl+C -> clipboard P2 (clean) ; P1 != P2 proves draw+undo work
#      Esc closes editor
#   C. capture, drag 300x200, Enter -> editor, Enter (Complete = copy+close) ->
#      editor gone, clipboard 300x200
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-m3-result.txt'
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder t, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder t, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static string[] List(int targetPid) {
        var res = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid) {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                RECT r; GetWindowRect(h, out r);
                res.Add(String.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                    h.ToInt64(), IsWindowVisible(h) ? 1 : 0, cls.ToString(), sb.ToString(),
                    r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
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
    public static string Titles(int targetPid) {
        var res = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                res.Add(sb.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return String.Join("|", res);
    }
}
'@

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Native -Namespace V

$exe = 'D:\EasySnipLite\src\EasySnipLite\bin\Debug\net10.0-windows\win-x64\EasySnipLite.exe'
$KEYUP = 2; $VK_CTRL = 0x11; $VK_SPACE = 0x20; $VK_RETURN = 0x0D; $VK_ESC = 0x1B; $VK_Z = 0x5A; $VK_C = 0x43
$tools = 'D:\EasySnipLite\tools'
foreach ($f in @('m3-annotated.png', 'm3-clean.png')) { Remove-Item (Join-Path $tools $f) -ErrorAction SilentlyContinue }

$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 2
if ($proc.HasExited) { Log "FAIL: app exited code=$($proc.ExitCode)"; exit 1 }
Log "OK: app started pid=$($proc.Id)"

function Invoke-Capture {
    [V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
    [V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Seconds 1
}

function Get-Windows($procId) { return [WinEnum]::List($procId) }

function Get-OverlayCount { return @((Get-Windows $proc.Id) | Where-Object { $_ -match '^[0-9]+\|1\|[^|]*\|EasySnipLite\|' }).Count }

# Editor window: visible, not fullscreen (overlay is fullscreen 1920x1080)
function Get-EditorWindow {
    $wins = Get-Windows $proc.Id
    return $wins | Where-Object {
        $parts = $_ -split '\|'
        $parts[1] -eq '1' -and [int]$parts[6] -gt 500 -and [int]$parts[6] -lt 1400 -and [int]$parts[7] -gt 400 -and [int]$parts[7] -lt 900
    } | Select-Object -First 1
}

function Drag-Region($x0, $y0, $dx, $dy) {
    [V.Native]::SetCursorPos($x0, $y0)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 10; $i++) {
        [V.Native]::SetCursorPos($x0 + $dx * $i / 10, $y0 + $dy * $i / 10)
        Start-Sleep -Milliseconds 20
    }
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

function Key-Shortcut($vk) {
    [V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($vk, 0, $KEYUP, [UIntPtr]::Zero)
    [V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

function Key-Tap($vk) {
    [V.Native]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($vk, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

function Save-Clipboard($path) {
    $img = [System.Windows.Forms.Clipboard]::GetImage()
    if ($null -eq $img) { Log "FAIL: no clipboard image at $path"; return $false }
    $img.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $img.Dispose()
    Log "OK: saved clipboard -> $path"
    return $true
}

function Assert-Size($path, $w, $h) {
    $img = [System.Drawing.Image]::FromFile($path)
    try {
        if ($img.Width -ne $w -or $img.Height -ne $h) {
            Log "FAIL: $path size $($img.Width)x$($img.Height), expected ${w}x${h}"; return $false
        }
        Log "OK: $path size ${w}x${h}"
        return $true
    } finally { $img.Dispose() }
}

function Images-Differ($a, $b) {
    $ia = [System.Drawing.Bitmap]::FromFile($a)
    $ib = [System.Drawing.Bitmap]::FromFile($b)
    try {
        if ($ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) { return $true }
        $rect = New-Object System.Drawing.Rectangle(0, 0, $ia.Width, $ia.Height)
        $da = $ia.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $db = $ib.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bytes = $da.Stride * $ia.Height
            $ba = New-Object byte[] $bytes
            $bb = New-Object byte[] $bytes
            [System.Runtime.InteropServices.Marshal]::Copy($da.Scan0, $ba, 0, $bytes)
            [System.Runtime.InteropServices.Marshal]::Copy($db.Scan0, $bb, 0, $bytes)
            for ($i = 0; $i -lt $bytes; $i++) { if ($ba[$i] -ne $bb[$i]) { return $true } }
            return $false
        } finally { $ia.UnlockBits($da); $ib.UnlockBits($db) }
    } finally { $ia.Dispose(); $ib.Dispose() }
}

# ================= Scenario A: Enter opens editor =================
Log '--- A: capture + Enter opens editor window ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
Key-Tap $VK_RETURN
$editor = Get-EditorWindow
if (-not $editor) { Log 'FAIL: editor window not opened after Enter'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log "OK: editor opened ($editor)"

# ================= Scenario B: draw rect, copy, undo, copy =================
Log '--- B: draw rect -> Ctrl+C annotated; Ctrl+Z -> Ctrl+C clean ---'
$cr = [WinEnum]::ClientRect(($editor -split '\|')[0])
$crp = $cr -split '\|'
$cx = [int]$crp[0]; $cy = [int]$crp[1]; $cw = [int]$crp[2]; $ch = [int]$crp[3]
Log "client origin=$cx,$cy size=${cw}x${ch}"

# Switch to Rect tool via keyboard (D2 = Rectangle), more reliable than pixel-clicking the button
Key-Tap 0x32   # D2
$titleAfterD2 = [WinEnum]::Titles($proc.Id)
Log "OK: pressed D2 (Rectangle tool); editor title contains Rectangle: $($titleAfterD2 -match 'Rectangle')"

# Canvas is top-left of client area (300x200 image at dpi=1); drag a rect around (120,90)-(170,130)
Drag-Region ($cx + 120) ($cy + 90) 50 40

Key-Shortcut $VK_C   # copy annotated
if (-not (Save-Clipboard (Join-Path $tools 'm3-annotated.png'))) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Size (Join-Path $tools 'm3-annotated.png') 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }

Key-Shortcut $VK_Z   # undo rect
Key-Shortcut $VK_C   # copy clean
if (-not (Save-Clipboard (Join-Path $tools 'm3-clean.png'))) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Size (Join-Path $tools 'm3-clean.png') 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }

if (-not (Images-Differ (Join-Path $tools 'm3-annotated.png') (Join-Path $tools 'm3-clean.png'))) {
    Log 'FAIL: annotated == clean (draw or undo did not work)'; Stop-Process -Id $proc.Id -Force; exit 1
}
Log 'OK: annotated != clean (rect drawn and undo removed it)'

Key-Tap $VK_ESC     # close editor without completing
Start-Sleep -Milliseconds 500
if (Get-EditorWindow) { Log 'FAIL: editor still open after Esc'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: Esc closed editor'

# ================= Scenario C: Complete (Enter) copies and closes =================
Log '--- C: Enter (Complete) copies 300x200 and closes editor ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
Key-Tap $VK_RETURN
$editor = Get-EditorWindow
if (-not $editor) { Log 'FAIL: editor not opened'; Stop-Process -Id $proc.Id -Force; exit 1 }
Key-Tap $VK_RETURN   # Complete
Start-Sleep -Milliseconds 800
if (Get-EditorWindow) { Log 'FAIL: editor still open after Enter (Complete)'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: editor closed after Complete'

Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 500
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) { Log 'FAIL: no clipboard image after Complete'; exit 1 }
if ($img.Width -ne 300 -or $img.Height -ne 200) {
    Log "FAIL: clipboard image $($img.Width)x$($img.Height), expected 300x200"; exit 1
}
Log 'OK: clipboard image 300x200 after Complete'
$img.Dispose()

Log 'M3 verification PASSED'
