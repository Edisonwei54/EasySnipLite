# Issue #23 E2E verification: live annotation preview / toolbar persistence / selection adjust
# Scenarios:
#   A. hotkey -> drag region -> D2 -> drag rect INSIDE region with mouse held ->
#      screenshot mid-drag must show the red stroke LIVE (not only after release);
#      after release the annotation must be committed
#   B. hotkey -> drag region -> draw annotation (activates selection window) ->
#      toolbar must stay visible (not covered by mask) -> clicking toolbar must NOT
#      collapse the selection (no phantom re-selection)
#   C. hotkey -> drag region -> drag NW handle -> selection border must follow;
#      body-drag (Selection tool, no object) must MOVE the region
# Each scenario restarts the app (fresh session, no leftover state).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-issue23-result.txt'
$out = 'D:\EasySnipLite\tools\verify-issue23'
Remove-Item $log -ErrorAction SilentlyContinue
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
function Log($msg) { Add-Content -Path $log -Value $msg; Write-Host $msg }
$script:failCount = 0
function Assert($cond, $msg) {
    if ($cond) { Log "PASS: $msg" } else { Log "FAIL: $msg"; $script:failCount++ }
}

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
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
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
}
'@

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
'@ -Name Native -Namespace V

$exe = 'D:\EasySnipLite\src\EasySnipLite\bin\Debug\net10.0-windows\win-x64\EasySnipLite.exe'
$KEYUP = 2; $VK_CTRL = 0x11; $VK_SPACE = 0x20; $VK_2 = 0x32

# ---- pre-flight: environment sanity (cursor moves, screen capture works) ----
$pt = New-Object V.Native+POINT
[V.Native]::GetCursorPos([ref]$pt) | Out-Null
$origX = $pt.X; $origY = $pt.Y
[V.Native]::SetCursorPos(50, 50) | Out-Null
Start-Sleep -Milliseconds 200
[V.Native]::GetCursorPos([ref]$pt) | Out-Null
if ($pt.X -ne 50 -or $pt.Y -ne 50) { Log 'PRE-FLIGHT FAIL: cursor does not move (environment blocked)'; exit 2 }
[V.Native]::SetCursorPos($origX, $origY) | Out-Null
# screen capture can transiently fail for minutes (session/display flap) -> wait until stable
$capReady = $false
for ($i = 0; $i -lt 24; $i++) {
    try {
        $bf = New-Object System.Drawing.Bitmap(16, 16)
        $bg = [System.Drawing.Graphics]::FromImage($bf)
        $bg.CopyFromScreen(0, 0, 0, 0, $bf.Size)
        $bg.Dispose(); $bf.Dispose()
        $capReady = $true
        break
    } catch { Start-Sleep -Seconds 5 }
}
if (-not $capReady) { Log 'PRE-FLIGHT FAIL: screen capture blocked for 2min'; exit 2 }
Log 'OK: environment pre-flight passed'

function Start-App {
    Get-Process EasySnipLite -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    $p = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 2
    if ($p.HasExited) { Log "FAIL: app exited code=$($p.ExitCode)"; exit 1 }
    return $p
}

function Invoke-Capture {
    for ($try = 0; $try -lt 3; $try++) {
        [V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
        [V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
        [V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        [V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
        [V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        [V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
        Start-Sleep -Seconds 1
        # verify the overlay actually opened (input can flap); retry otherwise
        $visible = @((Get-Windows $script:proc.Id) | Where-Object { ($_ -split '\|')[1] -eq '1' }).Count
        if ($visible -gt 0) { return }
        Log "WARN: overlay not visible after hotkey (try $($try + 1))"
        Start-Sleep -Seconds 3
    }
    throw 'hotkey did not open overlay after 3 tries (input blocked?)'
}

function Get-Windows($procId) { return [WinEnum]::List($procId) }

function Get-ToolbarWindow {
    $wins = Get-Windows $script:proc.Id
    return $wins | Where-Object {
        $parts = $_ -split '\|'
        $parts[1] -eq '1' -and [int]$parts[6] -gt 300 -and [int]$parts[6] -lt 2000 -and [int]$parts[7] -gt 20 -and [int]$parts[7] -lt 80
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

function Key-Tap($vk) {
    [V.Native]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    [V.Native]::keybd_event($vk, 0, $KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

function Shot($name) {
    # environment guard: CopyFromScreen can transiently fail (session/display flap) -> retry
    $lastErr = $null
    for ($try = 0; $try -lt 6; $try++) {
        try {
            $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
            $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
            $g.Dispose()
            $path = Join-Path $out $name
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            return $path
        } catch {
            $lastErr = $_.Exception.Message
            Start-Sleep -Seconds 2
        }
    }
    throw "screen capture failed after retries: $lastErr"
}

function Pixel($path, $x, $y) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    try { return $bmp.GetPixel($x, $y) } finally { $bmp.Dispose() }
}

function Brightness($c) { return (0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B) }

# red stroke (WPF Colors.Red #FF0000) within a window around (cx,cy)
function Has-RedNear($path, $cx, $cy, $r) {
    for ($x = $cx - $r; $x -le $cx + $r; $x++) {
        for ($y = $cy - $r; $y -le $cy + $r; $y++) {
            $p = Pixel $path $x $y
            if ($p.R -gt 150 -and $p.R -gt $p.G + 60 -and $p.R -gt $p.B + 60) { return $true }
        }
    }
    return $false
}

# selection border / handle blue (#FF2D9CFF) within a window around (cx,cy)
function Has-BlueNear($path, $cx, $cy, $r) {
    for ($x = $cx - $r; $x -le $cx + $r; $x++) {
        for ($y = $cy - $r; $y -le $cy + $r; $y++) {
            $p = Pixel $path $x $y
            if ($p.B -gt 180 -and $p.G -gt 100 -and $p.R -lt 110) { return $true }
        }
    }
    return $false
}

try {
    # ---- Scenario A: live annotation preview ----
    Log '--- Scenario A: live preview mid-drag ---'
    $script:proc = Start-App
    Invoke-Capture
    Drag-Region 200 200 300 200
    Key-Tap $VK_2   # D2 -> Rectangle tool
    $before = Shot 'a-before-draw.png'
    Assert (-not (Has-RedNear $before 251 275 3)) 'no red stroke before drawing (baseline)'

    # drag rect from (250,250) to (330,300), keep mouse DOWN, screenshot mid-drag
    [V.Native]::SetCursorPos(250, 250)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 8; $i++) {
        [V.Native]::SetCursorPos(250 + 80 * $i / 8, 250 + 50 * $i / 8)
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 250
    $mid = Shot 'a-mid-drag.png'
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
    $after = Shot 'a-after-release.png'
    Assert (Has-RedNear $mid 251 275 3) 'red stroke visible LIVE mid-drag (not only after release)'
    Assert (Has-RedNear $after 251 275 3) 'annotation committed after release'
    Stop-Process -Id $script:proc.Id -Force

    # ---- Scenario B: toolbar persistence + no phantom re-selection ----
    Log '--- Scenario B: toolbar stays above selection, clicks do not collapse ---'
    $script:proc = Start-App
    Invoke-Capture
    Drag-Region 200 200 300 200
    Start-Sleep -Milliseconds 400
    $tb = Get-ToolbarWindow
    if (-not $tb) { Log 'FAIL: toolbar not shown after selection'; throw 'toolbar missing' }
    $tp = ($tb -split '\|')
    $tcx = [int]$tp[4] + [int]$tp[6] / 2
    $tcy = [int]$tp[5] + [int]$tp[7] / 2
    Log "OK: toolbar rect $($tp[4]),$($tp[5]) $($tp[6])x$($tp[7]), center $tcx,$tcy"
    $shot0 = Shot 'b-toolbar-before-draw.png'
    $p0 = Pixel $shot0 $tcx $tcy
    Log "toolbar center before draw: brightness $([math]::Round((Brightness $p0)))"

    # draw an annotation -> selection window activates -> must NOT cover the toolbar
    Key-Tap $VK_2
    Drag-Region 250 250 100 60
    Start-Sleep -Milliseconds 400
    $shot1 = Shot 'b-toolbar-after-draw.png'
    $p1 = Pixel $shot1 $tcx $tcy
    Log "toolbar center after draw: brightness $([math]::Round((Brightness $p1)))"
    Assert ((Brightness $p1) -ge (Brightness $p0) - 15) 'toolbar not dimmed/covered after drawing'

    # click the toolbar -> must switch tool, NOT collapse the selection
    $selPix0 = Pixel $shot1 350 300
    [V.Native]::SetCursorPos($tcx, $tcy)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    $shot2 = Shot 'b-after-toolbar-click.png'
    $selPix2 = Pixel $shot2 350 300
    Log "selection center before click: brightness $([math]::Round((Brightness $selPix0))), after: $([math]::Round((Brightness $selPix2)))"
    Assert ((Brightness $selPix2) -ge (Brightness $selPix0) - 30) 'selection survives toolbar click (no phantom re-selection)'
    Stop-Process -Id $script:proc.Id -Force

    # ---- Scenario C: selection adjust ----
    Log '--- Scenario C: handle resize + body move ---'
    $script:proc = Start-App
    Invoke-Capture
    Drag-Region 200 200 300 200
    Start-Sleep -Milliseconds 300
    # 3a: drag NW corner (200,200) -> (150,150); border must follow
    [V.Native]::SetCursorPos(200, 200)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 8; $i++) {
        [V.Native]::SetCursorPos(200 - 50 * $i / 8, 200 - 50 * $i / 8)
        Start-Sleep -Milliseconds 25
    }
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    $resized = Shot 'c-resized.png'
    Assert (Has-BlueNear $resized 150 150 6) 'NW handle resize moves the selection border'
    Stop-Process -Id $script:proc.Id -Force

    $script:proc = Start-App
    Invoke-Capture
    Drag-Region 200 200 300 200
    Start-Sleep -Milliseconds 300
    # 3b: body drag (Selection tool default, no objects) must MOVE the region
    [V.Native]::SetCursorPos(350, 300)
    Start-Sleep -Milliseconds 150
    [V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 8; $i++) {
        [V.Native]::SetCursorPos(350 + 50 * $i / 8, 300 + 50 * $i / 8)
        Start-Sleep -Milliseconds 25
    }
    [V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    $moved = Shot 'c-moved.png'
    Assert (Has-BlueNear $moved 250 250 6) 'body drag (Selection tool) moves the region'
    Stop-Process -Id $script:proc.Id -Force
} catch {
    Log "EXCEPTION: $($_.Exception.Message)"
    Log "AT: $($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)"
    $script:failCount++
} finally {
    Get-Process EasySnipLite -ErrorAction SilentlyContinue | Stop-Process -Force
    $errLog = 'D:\EasySnipLite\error.log'
    if (Test-Path $errLog) { Log "FAIL: error.log exists (size $(Get-Item $errLog | Select-Object -ExpandProperty Length))" }
    else { Log 'OK: no error.log' }
    if ($script:failCount -gt 0) { Log "RESULT: FAILED ($($script:failCount) assertion(s) failed)" }
    else { Log 'RESULT: PASSED - all issue-23 scenarios verified' }
    exit $script:failCount
}
