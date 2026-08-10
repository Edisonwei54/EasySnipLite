using System.Windows.Input;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>热键本地化显示（纯逻辑）。键名不本地化（英文键名三语共用）；「双击」字样由调用方传入 resx 文本。</summary>
public static class HotkeyFormat
{
    public static string Format(HotkeySpec spec, string doubleTapText)
    {
        var mods = ModifierText(spec.Modifiers);
        var key = KeyText(spec.VirtualKey);
        if (spec.Kind == HotkeyKind.Chord)
            return mods.Length == 0 ? $"{doubleTapText} {key}" : $"{mods} + {doubleTapText} {key}";
        return mods.Length == 0 ? key : $"{mods} + {key}";
    }

    public static string ModifierText(HotkeyModifiers mods)
    {
        var parts = new List<string>();
        if ((mods & HotkeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((mods & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((mods & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        return string.Join(" + ", parts);
    }

    public static string KeyText(int vk)
    {
        // 主键盘数字 0-9：Key 枚举名是 D0-D9，直接取字符更友好
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        return KeyInterop.KeyFromVirtualKey(vk).ToString();
    }
}
