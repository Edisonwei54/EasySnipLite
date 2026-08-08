# M2 E2E verification: handles / body drag / nudge / Esc / Ctrl+S save / Enter copy
# Scenarios (all driven by global mouse/keyboard input):
#   A. drag 300x200, drag SE handle (+40,+30) -> Ctrl+S save -> PNG 340x230
#   B. drag 300x200, Left x3 -> Ctrl+S save -> PNG 300x200, differs from baseline D
#   C. drag 300x200, drag inside (+40,+20) -> Ctrl+S save -> PNG 300x200, differs from D
#   D. drag 300x200, Ctrl+S save -> baseline 300x200
#   E. drag, Esc (selection cleared, overlay stays) -> Esc (overlay gone)
#   F. drag, Enter -> overlay gone, clipboard image 300x200
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-m2-result.txt'
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
    public static string[] List(int targetPid) {
        var res = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid) {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                res.Add(String.Format("{0}|{1}|{2}|{3}", h.ToInt64(), IsWindowVisible(h) ? 1 : 0, cls.ToString(), sb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return res.ToArray();
    }
}
'@

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Native -Namespace V

$exe = 'D:\EasySnipLite\src\EasySnipLite\bin\Debug\net10.0-windows\win-x64\EasySnipLite.exe'
$KEYUP = 2; $VK_CTRL = 0x11; $VK_SPACE = 0x20; $VK_LEFT = 0x25; $VK_RETURN = 0x0D
$tools = 'D:\EasySnipLite\tools'
$files = @('m2-handle.png', 'm2-nudge.png', 'm2-move.png', 'm2-base.png')
foreach ($f in $files) { Remove-Item (Join-Path $tools $f) -ErrorAction SilentlyContinue }

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

function Get-OverlayCount {
    $wins = [WinEnum]::List($proc.Id)
    return @($wins | Where-Object { $_ -match '^[0-9]+\|1\|[^|]*\|EasySnipLite$' }).Count
}

function Drag-Region($x0, $y0, $dx, $dy) {
    [V.Native]::SetCursorPos($x0, $y0)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    for ($i = 1; $i -le 10; $i++) {
        [V.Native]::SetCursorPos($x0 + $dx * $i / 10, $y0 + $dy * $i / 10)
        Start-Sleep -Milliseconds 20
    }
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Start-Sleep -Milliseconds 300
}

function Mouse-Drag($x0, $y0, $x1, $y1) {
    [V.Native]::SetCursorPos($x0, $y0)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [V.Native]::SetCursorPos($x1, $y1)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

# Ctrl+S opens a modal SaveFileDialog; wait for it, type full path, Enter.
function Save-CurrentSelection($path) {
    [V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event(0x53, 0, 0, [UIntPtr]::Zero)  # S
    [V.Native]::keybd_event(0x53, 0, $KEYUP, [UIntPtr]::Zero)
    [V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1200
    # wait for a #32770 dialog owned by the app
    $dlg = $null
    for ($i = 0; $i -lt 20; $i++) {
        $wins = [WinEnum]::List($proc.Id)
        $dlg = $wins | Where-Object { $_ -match '^\d+\|1\|#32770\|' } | Select-Object -First 1
        if ($dlg) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $dlg) { Log "FAIL: no save dialog for $path"; return $false }
    Log "OK: save dialog visible ($dlg)"
    # paste the full path into the filename box (select-all first), then Enter
    [System.Windows.Forms.Clipboard]::SetText($path)
    [V.Native]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)  # Ctrl down
    [V.Native]::keybd_event(0x41, 0, 0, [UIntPtr]::Zero)  # A down
    [V.Native]::keybd_event(0x41, 0, $KEYUP, [UIntPtr]::Zero)
    [V.Native]::keybd_event(0x56, 0, 0, [UIntPtr]::Zero)  # V down
    [V.Native]::keybd_event(0x56, 0, $KEYUP, [UIntPtr]::Zero)
    [V.Native]::keybd_event(0x11, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    [V.Native]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)  # Enter
    [V.Native]::keybd_event(0x0D, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Seconds 2
    if (-not (Test-Path $path)) { Log "FAIL: file not created: $path"; return $false }
    return $true
}

function Assert-Png($path, $w, $h) {
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
            $stride = $da.Stride
            $bytes = $stride * $ia.Height
            $ba = New-Object byte[] $bytes
            $bb = New-Object byte[] $bytes
            [System.Runtime.InteropServices.Marshal]::Copy($da.Scan0, $ba, 0, $bytes)
            [System.Runtime.InteropServices.Marshal]::Copy($db.Scan0, $bb, 0, $bytes)
            for ($i = 0; $i -lt $bytes; $i++) {
                if ($ba[$i] -ne $bb[$i]) { return $true }
            }
            return $false
        } finally { $ia.UnlockBits($da); $ib.UnlockBits($db) }
    } finally { $ia.Dispose(); $ib.Dispose() }
}

# ================= Scenario A: handle resize + Ctrl+S save =================
Log '--- A: handle resize (SE +40,+30) -> save 340x230 ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
Mouse-Drag 700 500 740 530   # SE handle drag
if (-not (Save-CurrentSelection (Join-Path $tools 'm2-handle.png'))) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Png (Join-Path $tools 'm2-handle.png') 340 230)) { Stop-Process -Id $proc.Id -Force; exit 1 }

# ================= Scenario B: arrow-key nudge (Left x3) =================
Log '--- B: nudge Left x3 -> save 300x200 ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
for ($i = 0; $i -lt 3; $i++) {
    [V.Native]::keybd_event($VK_LEFT, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($VK_LEFT, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}
if (-not (Save-CurrentSelection (Join-Path $tools 'm2-nudge.png'))) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Png (Join-Path $tools 'm2-nudge.png') 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }

# ================= Scenario C: body drag (+40,+20) =================
Log '--- C: body drag inside (+40,+20) -> save 300x200 ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
Mouse-Drag 500 400 540 420   # inside the selection
if (-not (Save-CurrentSelection (Join-Path $tools 'm2-move.png'))) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Png (Join-Path $tools 'm2-move.png') 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }

# ================= Scenario D: baseline (no adjust) =================
Log '--- D: baseline save 300x200 ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
if (-not (Save-CurrentSelection (Join-Path $tools 'm2-base.png'))) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Png (Join-Path $tools 'm2-base.png') 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }

# nudge and body-drag must have changed the captured content
if (-not (Images-Differ (Join-Path $tools 'm2-base.png') (Join-Path $tools 'm2-nudge.png'))) {
    Log 'FAIL: nudge did not change captured content'; Stop-Process -Id $proc.Id -Force; exit 1
}
Log 'OK: nudge changed content (m2-base vs m2-nudge)'
if (-not (Images-Differ (Join-Path $tools 'm2-base.png') (Join-Path $tools 'm2-move.png'))) {
    Log 'FAIL: body drag did not change captured content'; Stop-Process -Id $proc.Id -Force; exit 1
}
Log 'OK: body drag changed content (m2-base vs m2-move)'

# ================= Scenario E: Esc clears selection, Esc again cancels =================
Log '--- E: Esc twice (clear selection, then cancel session) ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
[V.Native]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)   # Esc
[V.Native]::keybd_event(0x1B, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
$afterFirst = Get-OverlayCount
if ($afterFirst -eq 0) { Log 'FAIL: overlay closed after first Esc (should only clear selection)'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log "OK: first Esc cleared selection, overlay still open (count=$afterFirst)"
[V.Native]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event(0x1B, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
if ((Get-OverlayCount) -gt 0) { Log 'FAIL: overlay still open after second Esc'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: second Esc closed the session'

# ================= Scenario F: Enter copies to clipboard =================
Log '--- F: Enter copies 300x200 to clipboard ---'
Invoke-Capture
if ((Get-OverlayCount) -eq 0) { Log 'FAIL: overlay not shown'; Stop-Process -Id $proc.Id -Force; exit 1 }
Drag-Region 400 300 300 200
[V.Native]::keybd_event($VK_RETURN, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_RETURN, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1
if ((Get-OverlayCount) -gt 0) { Log 'FAIL: overlay still open after Enter'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: overlay closed after Enter'

Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 500
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) {
    Log 'FAIL: no image in clipboard after Enter'; exit 1
}
if ($img.Width -ne 300 -or $img.Height -ne 200) {
    Log "FAIL: clipboard image $($img.Width)x$($img.Height), expected 300x200"; exit 1
}
Log "OK: clipboard image 300x200"
$img.Dispose()

Log 'M2 verification PASSED'
