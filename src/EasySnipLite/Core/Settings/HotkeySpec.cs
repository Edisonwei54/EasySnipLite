using EasySnipLite.Core.Native;

namespace EasySnipLite.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

public enum HotkeyKind
{
    Chord,  // 双击时序（截图热键语义）
    Combo,  // 单键组合（穿透热键语义）
}

public sealed record HotkeySpec(HotkeyKind Kind, HotkeyModifiers Modifiers, int VirtualKey)
{
    public static HotkeySpec DefaultScreenshot =>
        new(HotkeyKind.Chord, HotkeyModifiers.Ctrl, Win32.VK_SPACE);
    public static HotkeySpec DefaultPassthrough =>
        new(HotkeyKind.Combo, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, Win32.VK_P);

    /// <summary>目标键与修饰键相同即视为冲突（chord 双击与 combo 单击语义不同，但同键同修饰会互相干扰）。</summary>
    public bool ConflictsWith(HotkeySpec other) =>
        Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
}
