using EasySnipLite.Core.Settings;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>修饰键精确匹配：声明的必须按下、未声明的必须未按下（与 RegisterHotKey 语义一致，避免误触发）。</summary>
public static class ModifierMatch
{
    public static bool IsMatch(HotkeyModifiers required, KeyEvent e)
    {
        var actual = HotkeyModifiers.None;
        if (e.CtrlDown) actual |= HotkeyModifiers.Ctrl;
        if (e.ShiftDown) actual |= HotkeyModifiers.Shift;
        if (e.AltDown) actual |= HotkeyModifiers.Alt;
        return actual == required;
    }
}
