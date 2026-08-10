using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Tests;

public class ChordDetectorTests
{
    private const int VkSpace = 0x20;
    private const int VkShift = 0x10;
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);

    private static KeyEvent SpaceUp(bool ctrl, DateTime t) =>
        new(KeyEventType.KeyUp, VkSpace, ctrl, false, false, t);

    private static KeyEvent SpaceDown(DateTime t) =>
        new(KeyEventType.KeyDown, VkSpace, true, false, false, t);

    private static KeyEvent OtherUp(DateTime t) =>
        new(KeyEventType.KeyUp, VkShift, true, false, false, t);

    [Fact]
    public void CtrlHeld_DoubleSpaceWithinWindow_Fires()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceDown(t0)));
        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(SpaceDown(t0 + TimeSpan.FromMilliseconds(100))));
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(150))));
    }

    [Fact]
    public void SinglePress_DoesNotFire()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        // 隔了很久才按第二次 —— 不再是“双击”
        Assert.False(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public void PressIntervalExceedingWindow_DoesNotFire()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(400))));
    }

    [Fact]
    public void DoubleSpaceWithoutCtrl_DoesNotFire()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(false, t0)));
        Assert.False(detector.HandleKey(SpaceUp(false, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void TriplePress_FiresOnlyOnce_ThenResets()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(100))));
        // 触发后重置：紧接着的第三次不重复触发
        Assert.False(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(200))));
    }

    [Fact]
    public void OtherKeysBetweenPresses_DoNotInterfere()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(OtherUp(t0 + TimeSpan.FromMilliseconds(50))));
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(120))));
    }

    [Fact]
    public void SpaceKeyDown_IsIgnored_AndDoesNotPolluteTiming()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceDown(t0)));
        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(SpaceDown(t0 + TimeSpan.FromMilliseconds(80))));
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void AltModifier_DoubleSpaceWithinWindow_Fires()
    {
        var detector = new ChordDetector(Window, VkSpace, HotkeyModifiers.Alt);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, false, false, true, t0)));
        Assert.True(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, false, false, true, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void CtrlShiftModifier_BothRequired_Fires()
    {
        var detector = new ChordDetector(Window, VkSpace, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, true, true, false, t0)));
        Assert.True(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, true, true, false, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void UndeclaredModifierDown_DoesNotFire()
    {
        var detector = new ChordDetector(Window, VkSpace, HotkeyModifiers.Ctrl);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, true, true, false, t0)));
        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, true, true, false, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void NoModifier_BareDoubleTap_Fires()
    {
        var detector = new ChordDetector(Window, VkSpace, HotkeyModifiers.None);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, false, false, false, t0)));
        Assert.True(detector.HandleKey(new(KeyEventType.KeyUp, VkSpace, false, false, false, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void CustomTargetKey_Fires()
    {
        const int vkA = 0x41;
        var detector = new ChordDetector(Window, vkA, HotkeyModifiers.Ctrl);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, vkA, true, false, false, t0)));
        Assert.True(detector.HandleKey(new(KeyEventType.KeyUp, vkA, true, false, false, t0 + TimeSpan.FromMilliseconds(100))));
    }

    [Fact]
    public void ModifierMissingOnSecondTap_DoesNotFire_AndDoesNotPolluteTiming()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(SpaceUp(false, t0 + TimeSpan.FromMilliseconds(100)))); // Ctrl 缺失：不触发
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(200))));   // 时序未被污染：与第一击仍在窗口内
    }

    [Fact]
    public void InterleavedModifierKeyUp_BetweenTaps_DoesNotPollute()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkShift, true, false, false, t0 + TimeSpan.FromMilliseconds(50)))); // Shift 释放夹杂
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(100))));
    }
}
