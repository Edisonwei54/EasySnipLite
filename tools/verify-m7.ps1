# M7 smoke: publish single-file -> run dist exe -> hotkey capture -> clipboard -> no error.log
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-m7-result.txt'
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
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    public static string[] List(int targetPid) {
        var res = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid) {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                res.Add(String.Format("{0}|{1}|{2}", h.ToInt64(), IsWindowVisible(h) ? 1 : 0, sb.ToString()));
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

$dist = 'D:\EasySnipLite\dist'
$exe = Join-Path $dist 'EasySnipLite.exe'

# 1. publish single-file
Log 'STEP: publish single-file (Release, win-x64)...'
& dotnet publish 'D:\EasySnipLite\src\EasySnipLite' -c Release -r win-x64 -o $dist | Out-Null
if ($LASTEXITCODE -ne 0) { Log "FAIL: publish exit=$LASTEXITCODE"; exit 1 }
if (-not (Test-Path $exe)) { Log 'FAIL: dist exe missing'; exit 1 }
if (Test-Path (Join-Path $dist 'EasySnipLite.dll')) { Log 'FAIL: EasySnipLite.dll present (not single-file)'; exit 1 }
Log 'OK: published single-file exe'

# 2. clean error.log from previous runs
$errorLog = Join-Path $env:APPDATA 'EasySnipLite\error.log'
Remove-Item $errorLog -ErrorAction SilentlyContinue

# 3. start dist exe
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 2
if ($proc.HasExited) { Log "FAIL: app exited code=$($proc.ExitCode)"; exit 1 }
Log "OK: app started pid=$($proc.Id)"

# 4. Ctrl + double-tap Space -> capture overlay
$KEYUP = 2; $VK_CTRL = 0x11; $VK_SPACE = 0x20
[V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
[V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 100
[V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1

$wins = [WinEnum]::List($proc.Id)
$overlay = $wins | Where-Object { $_ -match '^[0-9]+\|1\|EasySnipLite$' }
if ($overlay.Count -eq 0) { Log "FAIL: no visible overlay. all=$($wins -join '; ')"; Stop-Process -Id $proc.Id -Force; exit 1 }
Log "OK: overlay visible (count=$($overlay.Count))"

# 5. drag 300x200 region and confirm with Enter
$x0 = 400; $y0 = 300
[V.Native]::SetCursorPos($x0, $y0)
Start-Sleep -Milliseconds 150
[V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
for ($i = 1; $i -le 10; $i++) {
    [V.Native]::SetCursorPos($x0 + 30 * $i, $y0 + 20 * $i)
    Start-Sleep -Milliseconds 20
}
[V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
Start-Sleep -Milliseconds 300
Log 'OK: drag done'
[V.Native]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event(0x0D, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1

if ($proc.HasExited) { Log "FAIL: app crashed after Enter code=$($proc.ExitCode)"; exit 1 }
$winsAfter = [WinEnum]::List($proc.Id)
$overlayAfter = $winsAfter | Where-Object { $_ -match '^[0-9]+\|1\|EasySnipLite$' }
if ($overlayAfter.Count -gt 0) { Log 'FAIL: overlay still open after Enter'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: overlay closed after Enter'

# 5b. since M3, Enter opens the editor; press Enter again (Complete = copy + close)
[V.Native]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event(0x0D, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1
if ($proc.HasExited) { Log "FAIL: app crashed after Complete code=$($proc.ExitCode)"; exit 1 }
Log 'OK: Complete copied and closed editor'

# 6. clean run must NOT create error.log
if (Test-Path $errorLog) { Log 'FAIL: error.log created on clean run'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: no error.log on clean run'

# kill app first, then read clipboard (proves data physically copied)
Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 500

# 7. clipboard verification
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) {
    Log 'FAIL: no image in clipboard'
    $formats = [System.Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join ','
    Log "INFO: formats: $formats"
    exit 1
}
Log "OK: clipboard image $($img.Width)x$($img.Height)"
if ($img.Width -lt 280 -or $img.Width -gt 320 -or $img.Height -lt 180 -or $img.Height -gt 220) {
    Log "FAIL: clipboard size $($img.Width)x$($img.Height), expected ~300x200"; exit 1
}
Log 'OK: clipboard size ~300x200'
$out = 'D:\EasySnipLite\tools\m7-capture.png'
$img.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Log "OK: saved $out"
Log 'M7 verification PASSED'
