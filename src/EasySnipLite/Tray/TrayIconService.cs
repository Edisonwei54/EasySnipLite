using System.Drawing;
using System.Windows.Forms;

namespace EasySnipLite.Tray;

/// <summary>
/// 托盘常驻图标（WinForms NotifyIcon 互操作）。M1 最小版：区域截图 / 退出；
/// M5 补全长截图、设置等菜单。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? CaptureRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _icon = new NotifyIcon
        {
            Text = "EasySnipLite",
            Icon = SystemIcons.Application, // M5 换应用图标资源
            Visible = true,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("区域截图 (Ctrl+双击空格)", null, (_, _) => CaptureRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
