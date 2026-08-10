using EasySnipLite.Core.Native;

namespace EasySnipLite.Core.Settings;

/// <summary>修饰键虚拟键码判定：通用码（0x10/0x11/0x12）与左右专用码（0xA0-0xA5）都算。
/// 低级钩子对修饰键报左右专用码（如 VK_LCONTROL），仅认通用码会把修饰键当普通键。</summary>
public static class ModifierKey
{
    public static bool IsModifier(int vk) =>
        vk is Win32.VK_SHIFT or Win32.VK_CONTROL or Win32.VK_MENU
        or Win32.VK_LSHIFT or Win32.VK_RSHIFT
        or Win32.VK_LCONTROL or Win32.VK_RCONTROL
        or Win32.VK_LMENU or Win32.VK_RMENU;
}
