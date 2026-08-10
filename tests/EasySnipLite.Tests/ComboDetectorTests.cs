using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Tests;

public class ComboDetectorTests
{
    private const int VkP = 0x50;
    private const int VkShift = 0x10;

    private static ComboDetector Default() =>
        new(VkP, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);

    private static KeyEvent Down(int vk, bool ctrl = false, bool shift = false, bool alt = false) =>
        new(KeyEventType.KeyDown, vk, ctrl, shift, alt, DateTime.UtcNow);

    private static KeyEvent Up(int vk, bool ctrl = false, bool shift = false, bool alt = false) =>
        new(KeyEventType.KeyUp, vk, ctrl, shift, alt, DateTime.UtcNow);

    [Fact]
    public void KeyDown_WithMatchingModifiers_Fires()
    {
        Assert.True(Default().HandleKey(Down(VkP, ctrl: true, shift: true)));
    }

    [Fact]
    public void KeyDown_WrongModifiers_DoesNotFire()
    {
        Assert.False(Default().HandleKey(Down(VkP, ctrl: true)));
        Assert.False(Default().HandleKey(Down(VkP, shift: true)));
        Assert.False(Default().HandleKey(Down(VkP)));
    }

    [Fact]
    public void KeyUp_DoesNotFire()
    {
        var detector = Default();
        Assert.False(detector.HandleKey(Up(VkP, ctrl: true, shift: true)));
    }

    [Fact]
    public void HeldKey_RepeatedKeyDown_FiresOnlyOnceUntilRelease()
    {
        var detector = Default();
        Assert.True(detector.HandleKey(Down(VkP, ctrl: true, shift: true)));
        Assert.False(detector.HandleKey(Down(VkP, ctrl: true, shift: true))); // auto-repeat
        detector.HandleKey(Up(VkP, ctrl: true, shift: true));
        Assert.True(detector.HandleKey(Down(VkP, ctrl: true, shift: true))); // 重新按压可再触发
    }

    [Fact]
    public void OtherKey_DoesNotFire()
    {
        Assert.False(Default().HandleKey(Down(VkShift, ctrl: true, shift: true)));
    }

    [Fact]
    public void CustomSpec_FiresOnItsOwnCombo()
    {
        var detector = new ComboDetector(0x41, HotkeyModifiers.Alt); // Alt+A
        Assert.True(detector.HandleKey(Down(0x41, alt: true)));
        Assert.False(detector.HandleKey(Down(0x41, ctrl: true)));
    }

    [Fact]
    public void ExtraModifierBeyondDeclared_DoesNotFire()
    {
        var detector = Default(); // 声明 Ctrl+Shift
        Assert.False(detector.HandleKey(Down(VkP, ctrl: true, shift: true, alt: true))); // 多按 Alt：精确匹配拒绝
    }
}
