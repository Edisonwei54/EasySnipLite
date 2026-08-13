# Issue #20 E2E verification: inline annotation overlay (no editor window)
# Scenarios:
#   A. hotkey -> drag 300x200 -> mouse-up -> toolbar window visible below region
#      Enter (complete = copy+close) -> clipboard 300x200 clean ref
#   B. hotkey -> drag -> D2 (Rect tool) -> drag inside region (annotation)
#      Enter -> clipboard 300x200 annotated ; differs from clean ref
#   C. hotkey -> drag -> D2 -> drag rect -> Esc (clear annotations) -> Enter
#      clipboard equals clean ref (Esc level-1 works)
#   D. hotkey -> drag -> Esc (clear selection) -> Esc (cancel session) ->
#      no overlay/toolbar windows left
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-issue20-result.txt'
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
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Native -Namespace V

$exe = 'D:\EasySnipLite\src\EasySnipLite\bin\Debug\net10.0-windows\win-x64\EasySnipLite.exe'
$KEYUP = 2; $VK_CTRL = 0x11; $VK_SPACE = 0x20; $VK_RETURN = 0x0D; $VK_ESC = 0x1B; $VK_Z = 0x5A; $VK_2 = 0x32
$tools = 'D:\EasySnipLite\tools'
foreach ($f in @('issue20-clean.png', 'issue20-annotated.png', 'issue20-cleared.png')) { Remove-Item (Join-Path $tools $f) -ErrorAction SilentlyContinue }

# Pitfall: leftover app process from a failed run must be killed first
$leftover = Get-Process EasySnipLite -ErrorAction SilentlyContinue
if ($leftover) { $leftover | Stop-Process -Force; Start-Sleep -Seconds 1; Log "WARN: killed leftover EasySnipLite process" }

$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 2
if ($proc.HasExited) { Log "FAIL: app exited code=$($proc.ExitCode)"; exit 1 }
Log "OK: app started pid=$($proc.Id)"

function Invoke-Capture {
    # 环境防抖：输入注入可能被会话波动吞掉 -> 重试并验证遮罩出现
    for ($try = 0; $try -lt 4; $try++) {
        [V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
        [V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
        [V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        [V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
        [V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        [V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
        Start-Sleep -Seconds 1
        $visible = @((Get-Windows $proc.Id) | Where-Object { ($_ -split '\|')[1] -eq '1' }).Count
        if ($visible -gt 0) { return }
        Log "WARN: overlay not visible after hotkey (try $($try + 1))"
        Start-Sleep -Seconds 3
    }
    throw 'hotkey did not open overlay after 4 tries'
}

function Get-Windows($procId) { return [WinEnum]::List($procId) }

function Get-ToolbarWindow {
    $wins = Get-Windows $proc.Id
    return $wins | Where-Object {
        $parts = $_ -split '\|'
        $parts[1] -eq '1' -and [int]$parts[6] -gt 300 -and [int]$parts[6] -lt 1200 -and [int]$parts[7] -gt 20 -and [int]$parts[7] -lt 60
    } | Select-Object -First 1
}

function Get-OverlayCount {
    return @((Get-Windows $proc.Id) | Where-Object { $_ -match '^[0-9]+\|1\|[^|]*\|EasySnipLite\|' -and [int]($_ -split '\|')[6] -gt 1000 }).Count
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
    # Enter 完成后复制与关窗解耦（OnConfirm 先关会话再写剪贴板）→ 读取需容忍短暂延迟/剪贴板竞争
    $img = $null
    for ($i = 0; $i -lt 10; $i++) {
        $img = [System.Windows.Forms.Clipboard]::GetImage()
        if ($null -ne $img) { break }
        Start-Sleep -Milliseconds 500
    }
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
        for ($y = 0; $y -lt $ia.Height; $y += 3) {
            for ($x = 0; $x -lt $ia.Width; $x += 3) {
                if ($ia.GetPixel($x, $y) -ne $ib.GetPixel($x, $y)) { return $true }
            }
        }
        return $false
    } finally { $ia.Dispose(); $ib.Dispose() }
}

# ---- Scenario A: capture -> toolbar appears -> Enter copies clean region ----
Log '--- Scenario A: toolbar + complete (copy+close) ---'
try { [System.Windows.Forms.Clipboard]::Clear() } catch { }  # 清空剪贴板：避免读到上一场景的陈旧内容
Invoke-Capture
Drag-Region 200 200 300 200
$toolbar = Get-ToolbarWindow
if (-not $toolbar) { Log 'FAIL: toolbar window not shown after mouse-up'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log "OK: toolbar window visible: $toolbar"
Key-Tap $VK_RETURN
Start-Sleep -Milliseconds 500
if ((Get-OverlayCount) -ne 0) { Log 'FAIL: overlay still visible after Enter'; Stop-Process -Id $proc.Id -Force; exit 1 }
$clean = Join-Path $tools 'issue20-clean.png'
if (-not (Save-Clipboard $clean)) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Size $clean 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }

# ---- Scenario B: annotate inline -> Enter copies annotated ----
Log '--- Scenario B: inline rect annotation ---'
try { [System.Windows.Forms.Clipboard]::Clear() } catch { }  # 清空剪贴板：避免读到上一场景的陈旧内容
Invoke-Capture
Drag-Region 200 200 300 200
if (-not (Get-ToolbarWindow)) { Log 'FAIL: toolbar missing in scenario B'; Stop-Process -Id $proc.Id -Force; exit 1 }
Key-Tap $VK_2   # D2 -> Rectangle tool
Drag-Region 250 250 100 80   # draw rect inside region
Key-Tap $VK_RETURN
Start-Sleep -Milliseconds 500
$annotated = Join-Path $tools 'issue20-annotated.png'
if (-not (Save-Clipboard $annotated)) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Assert-Size $annotated 300 200)) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (-not (Images-Differ $clean $annotated)) {
    Log 'FAIL: annotated copy is identical to clean copy (annotation not drawn)'
    Stop-Process -Id $proc.Id -Force; exit 1
}
Log 'OK: annotated copy differs from clean copy'

# ---- Scenario C: Esc level-1 clears annotations -> Enter copies clean ----
Log '--- Scenario C: Esc clears annotations ---'
try { [System.Windows.Forms.Clipboard]::Clear() } catch { }  # 清空剪贴板：避免读到上一场景的陈旧内容
Invoke-Capture
Drag-Region 200 200 300 200
if (-not (Get-ToolbarWindow)) { Log 'FAIL: toolbar missing in scenario C'; Stop-Process -Id $proc.Id -Force; exit 1 }
Key-Tap $VK_2
Drag-Region 250 250 100 80
Key-Tap $VK_ESC   # level 1: clear annotations
Key-Tap $VK_RETURN
Start-Sleep -Milliseconds 500
$cleared = Join-Path $tools 'issue20-cleared.png'
if (-not (Save-Clipboard $cleared)) { Stop-Process -Id $proc.Id -Force; exit 1 }
if (Images-Differ $clean $cleared) {
    Log 'FAIL: after Esc annotations still present (copy differs from clean)'
    Stop-Process -Id $proc.Id -Force; exit 1
}
Log 'OK: Esc cleared annotations, copy matches clean ref'

# ---- Scenario D: Esc level-2/3 exits session ----
Log '--- Scenario D: Esc levels 2+3 ---'
Invoke-Capture
Drag-Region 200 200 300 200
Key-Tap $VK_ESC   # level 2: clear selection (no annotations drawn)
Key-Tap $VK_ESC   # level 3: cancel session
Start-Sleep -Milliseconds 500
if ((Get-OverlayCount) -ne 0) { Log 'FAIL: overlay still visible after double Esc'; Stop-Process -Id $proc.Id -Force; exit 1 }
if (Get-ToolbarWindow) { Log 'FAIL: toolbar still visible after double Esc'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: session cancelled, no overlay/toolbar windows'

# ---- error.log must be absent ----
$errLog = 'D:\EasySnipLite\error.log'
if (Test-Path $errLog) { Log "FAIL: error.log exists (size $(Get-Item $errLog | Select-Object -ExpandProperty Length))"; exit 1 }
Log 'OK: no error.log'

Stop-Process -Id $proc.Id -Force
Log 'PASSED: all issue-20 scenarios verified'
