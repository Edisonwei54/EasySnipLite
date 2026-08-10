using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Tests;

public class HotkeyFormatTests
{
    private const string DoubleTap = "double-tap";

    [Fact]
    public void Chord_FormatsWithDoubleTapText()
    {
        var spec = new HotkeySpec(HotkeyKind.Chord, HotkeyModifiers.Ctrl, 0x20); // Ctrl+双击Space
        Assert.Equal("Ctrl + double-tap Space", HotkeyFormat.Format(spec, DoubleTap));
    }

    [Fact]
    public void Combo_FormatsPlain()
    {
        var spec = new HotkeySpec(HotkeyKind.Combo, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x50);
        Assert.Equal("Ctrl + Shift + P", HotkeyFormat.Format(spec, DoubleTap));
    }

    [Fact]
    public void NoModifier_OmitsModifierPart()
    {
        var spec = new HotkeySpec(HotkeyKind.Chord, HotkeyModifiers.None, 0x20);
        Assert.Equal("double-tap Space", HotkeyFormat.Format(spec, DoubleTap));
        var combo = new HotkeySpec(HotkeyKind.Combo, HotkeyModifiers.None, 0x41);
        Assert.Equal("A", HotkeyFormat.Format(combo, DoubleTap));
    }

    [Fact]
    public void DigitKey_ShowsPlainDigit()
    {
        Assert.Equal("1", HotkeyFormat.KeyText(0x31));
        Assert.Equal("0", HotkeyFormat.KeyText(0x30));
        Assert.Equal("9", HotkeyFormat.KeyText(0x39));
    }

    [Fact]
    public void LetterKey_ShowsKeyName()
    {
        Assert.Equal("A", HotkeyFormat.KeyText(0x41));
        Assert.Equal("P", HotkeyFormat.KeyText(0x50));
        Assert.Equal("Space", HotkeyFormat.KeyText(0x20));
    }

    [Fact]
    public void ModifierText_ListsInOrder()
    {
        Assert.Equal("Ctrl + Shift + Alt", HotkeyFormat.ModifierText(
            HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt));
        Assert.Equal("", HotkeyFormat.ModifierText(HotkeyModifiers.None));
    }
}
