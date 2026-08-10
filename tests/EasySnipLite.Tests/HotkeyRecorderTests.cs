using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Tests;

public class HotkeyRecorderTests
{
    private const int VkSpace = 0x20;
    private const int VkEsc = 0x1B;
    private const int VkCtrl = 0x11;
    private const int VkP = 0x50;
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);

    private static KeyEvent Down(int vk, bool ctrl = false, bool shift = false, bool alt = false, DateTime? t = null) =>
        new(KeyEventType.KeyDown, vk, ctrl, shift, alt, t ?? DateTime.UtcNow);

    private static KeyEvent Up(int vk, bool ctrl = false, bool shift = false, bool alt = false, DateTime? t = null) =>
        new(KeyEventType.KeyUp, vk, ctrl, shift, alt, t ?? DateTime.UtcNow);

    [Fact]
    public void ComboMode_PressCombination_RecordsSpec()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Combo, Window);
        HotkeySpec? recorded = null;
        recorder.Recorded += spec => recorded = spec;

        recorder.HandleKey(Down(VkCtrl));
        recorder.HandleKey(Down(VkP, ctrl: true));

        Assert.NotNull(recorded);
        Assert.Equal(HotkeyKind.Combo, recorded.Kind);
        Assert.Equal(HotkeyModifiers.Ctrl, recorded.Modifiers);
        Assert.Equal(VkP, recorded.VirtualKey);
    }

    [Fact]
    public void ComboMode_ModifierKeysAlone_DoNotRecord()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Combo, Window);
        var fired = false;
        recorder.Recorded += _ => fired = true;

        recorder.HandleKey(Down(VkCtrl));
        recorder.HandleKey(Down(VkCtrl)); // 重复按修饰键
        recorder.HandleKey(Up(VkCtrl));

        Assert.False(fired);
    }

    [Fact]
    public void ChordMode_DoubleTapWithModifier_RecordsChordSpec()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Chord, Window);
        HotkeySpec? recorded = null;
        recorder.Recorded += spec => recorded = spec;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        recorder.HandleKey(Down(VkCtrl, t: t0));
        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0));
        recorder.HandleKey(Down(VkSpace, ctrl: true, t: t0 + TimeSpan.FromMilliseconds(50)));
        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0 + TimeSpan.FromMilliseconds(80)));

        Assert.NotNull(recorded);
        Assert.Equal(HotkeyKind.Chord, recorded.Kind);
        Assert.Equal(HotkeyModifiers.Ctrl, recorded.Modifiers);
        Assert.Equal(VkSpace, recorded.VirtualKey);
    }

    [Fact]
    public void ChordMode_SecondTapBeyondWindow_ResetsAndNeedsFreshDoubleTap()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Chord, Window);
        HotkeySpec? recorded = null;
        recorder.Recorded += spec => recorded = spec;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0));
        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0 + TimeSpan.FromSeconds(1))); // 超窗 → last-wins 重置
        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0 + TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(80))); // 第三次快速跟上

        Assert.NotNull(recorded); // 第二次+第三次构成新双击
    }

    [Fact]
    public void ChordMode_ModifiersChangedMidChord_Resets()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Chord, Window);
        HotkeySpec? recorded = null;
        recorder.Recorded += spec => recorded = spec;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0));
        recorder.HandleKey(Up(VkSpace, shift: true, t: t0 + TimeSpan.FromMilliseconds(100))); // 修饰键不同 → 重置

        Assert.Null(recorded);
    }

    [Fact]
    public void ChordMode_DifferentKeyBetweenTaps_LastWins()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Chord, Window);
        HotkeySpec? recorded = null;
        recorder.Recorded += spec => recorded = spec;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0));
        recorder.HandleKey(Up(VkP, ctrl: true, t: t0 + TimeSpan.FromMilliseconds(50))); // 不同键 → 候选换成 P
        recorder.HandleKey(Up(VkP, ctrl: true, t: t0 + TimeSpan.FromMilliseconds(80)));

        Assert.NotNull(recorded);
        Assert.Equal(VkP, recorded.VirtualKey);
    }

    [Fact]
    public void Esc_AnyTime_Cancels()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Combo, Window);
        var cancelled = false;
        recorder.Cancelled += () => cancelled = true;

        recorder.HandleKey(Down(VkEsc));

        Assert.True(cancelled);
    }

    [Fact]
    public void Esc_AfterRecordingStarted_StillCancels()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Chord, Window);
        var cancelled = false;
        recorder.Cancelled += () => cancelled = true;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        recorder.HandleKey(Up(VkSpace, ctrl: true, t: t0));
        recorder.HandleKey(Down(VkEsc, t: t0 + TimeSpan.FromMilliseconds(100)));

        Assert.True(cancelled);
    }
}
