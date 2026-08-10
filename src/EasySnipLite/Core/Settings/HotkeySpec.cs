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
