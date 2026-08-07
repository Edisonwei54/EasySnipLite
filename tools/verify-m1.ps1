# M1 E2E verification: hotkey -> overlay -> drag -> Enter -> clipboard PNG
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-m1-result.txt'
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

$exe = 'D:\EasySnipLite\src\EasySnipLite\bin\Debug\net10.0-windows\win-x64\EasySnipLite.exe'
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 2
if ($proc.HasExited) { Log "FAIL: app exited code=$($proc.ExitCode)"; exit 1 }
Log "OK: app started pid=$($proc.Id)"

# 1. Ctrl + double-tap Space
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

# 2. overlay window visible?
$wins = [WinEnum]::List($proc.Id)
$overlay = $wins | Where-Object { $_ -match '^[0-9]+\|1\|EasySnipLite$' }
if ($overlay.Count -eq 0) { Log "FAIL: no visible overlay. all=$($wins -join '; ')"; Stop-Process -Id $proc.Id -Force; exit 1 }
Log "OK: overlay visible (count=$($overlay.Count))"

# 3. drag a 300x200 region from screen center area
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
Log "OK: drag done ($x0,$y0) -> ($($x0+300),$($y0+200))"

# 4. Enter to confirm
[V.Native]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event(0x0D, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1

if ($proc.HasExited) { Log "FAIL: app crashed after Enter code=$($proc.ExitCode)"; exit 1 }
$winsAfter = [WinEnum]::List($proc.Id)
$overlayAfter = $winsAfter | Where-Object { $_ -match '^[0-9]+\|1\|EasySnipLite$' }
if ($overlayAfter.Count -gt 0) { Log 'FAIL: overlay still open after Enter'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: overlay closed after Enter'
# kill app first, then read clipboard (proves data physically copied)
Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 500

# 5. clipboard verification
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) {
    Log 'FAIL: no image in clipboard'
    $formats = [System.Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join ','
    Log "INFO: formats: $formats"
    Log "INFO: ContainsImage=$([System.Windows.Forms.Clipboard]::ContainsImage()) ContainsDIB=$([System.Windows.Forms.Clipboard]::ContainsData('DeviceIndependentBitmap')) ContainsPNG=$([System.Windows.Forms.Clipboard]::ContainsData('PNG'))"
    exit 1
}
Log "OK: clipboard image $($img.Width)x$($img.Height)"
$out = 'D:\EasySnipLite\tools\m1-capture.png'
$img.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Log "OK: saved $out"
Log 'M1 verification PASSED'
