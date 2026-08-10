using System.Drawing;
using System.Windows.Forms;
using EasySnipLite.Localization;

namespace EasySnipLite.Tray;

/// <summary>
/// 托盘常驻图标（WinForms NotifyIcon 互操作）。菜单：区域截图(热键) / 设置 / 退出。
/// 菜单代码构建，Rebuild() 支持语言/热键即时刷新。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();

    public event Action? CaptureRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _icon = new NotifyIcon
        {
            Text = "EasySnipLite",
            Icon = SystemIcons.Application, // M5 换应用图标资源
            Visible = true,
            ContextMenuStrip = _menu,
        };
        Rebuild(""); // App 启动装配后立刻以真实热键 Rebuild
    }

    /// <summary>重建菜单（语言/热键变化时调用）。captureHotkeyText 为格式化后的截图热键。</summary>
    public void Rebuild(string captureHotkeyText)
    {
        _menu.Items.Clear();
        _menu.Items.Add(
            string.Format(AppResources.TrayCaptureFormat, captureHotkeyText),
            null, (_, _) => CaptureRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(AppResources.TraySettings, null, (_, _) => SettingsRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(AppResources.TrayExit, null, (_, _) => ExitRequested?.Invoke());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
