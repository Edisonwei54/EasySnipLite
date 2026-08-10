using System.IO;
using System.Windows;

namespace EasySnipLite.Core.Diagnostics;

/// <summary>
/// 错误兜底助手（M7）：日志（error.log，超限归档 .old）+ 托盘气泡（App 注入 TrayNotify）+ 致命弹窗退出。
/// 所有路径尽力而为：日志/气泡失败一律静默，绝不让错误处理本身崩溃。
/// </summary>
public static class AppErrors
{
    /// <summary>日志文件路径（测试可注入临时目录）。</summary>
    public static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EasySnipLite", "error.log");

    /// <summary>日志上限：超过则归档 error.log.old 并重建（保留两份防膨胀）。</summary>
    public static long MaxLogSize { get; set; } = 512 * 1024;

    /// <summary>托盘气泡委托（App 装配注入 _tray.ShowBalloon）；未注入时仅记录日志。</summary>
    public static Action<string>? TrayNotify { get; set; }

    public static void Log(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSize)
                File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* 磁盘满/只读/被锁：日志尽力而为，静默 */ }
    }

    /// <summary>非致命错误：记录日志 + 托盘气泡提示（气泡失败静默）。</summary>
    public static void Notify(Exception ex, string message)
    {
        Log(ex);
        try { TrayNotify?.Invoke(message); } catch { /* 气泡尽力而为 */ }
    }

    /// <summary>致命错误：记录日志 + 弹窗告知 + 请求退出（进程可能即将终止，尽力而为）。</summary>
    public static void Fatal(Exception ex, string message)
    {
        Log(ex);
        try
        {
            MessageBox.Show(message, "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
        catch { /* 进程即将终止，尽力而为 */ }
    }
}
