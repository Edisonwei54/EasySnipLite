using Microsoft.Win32;

namespace EasySnipLite.Core.Settings;

/// <summary>开机自启（HKCU\...\CurrentVersion\Run）。写失败静默忽略，启动时按设置补写/删除。</summary>
public static class RegistryAutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EasySnipLite";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName)?.ToString() == Environment.ProcessPath;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled) key?.SetValue(ValueName, Environment.ProcessPath);
            else key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* 静默：下次启动再尝试 */ }
    }

    /// <summary>启动时按设置同步注册表状态。</summary>
    public static void Sync(bool autostart)
    {
        if (autostart && !IsEnabled()) SetEnabled(true);
        else if (!autostart && IsEnabled()) SetEnabled(false);
    }
}
