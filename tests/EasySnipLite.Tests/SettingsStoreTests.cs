using EasySnipLite.Core.Settings;
// 注意：命名空间 EasySnipLite.Settings（Settings/SettingsWindow）与类型 Settings 同名，
// 在 EasySnipLite.Tests 内简单名 Settings 会被 enclosing 命名空间成员抢走（CS0118），
// 故这里对类型用全限定 EasySnipLite.Core.Settings.Settings（同 CLAUDE.md 规则 6 的命名冲突）。

namespace EasySnipLite.Tests;

public class SettingsStoreTests
{
    private static string TempPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "easysniplite-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var path = TempPath();
        var original = new EasySnipLite.Core.Settings.Settings(
            AppLanguage.TraditionalChinese,
            @"C:\Users\test\Pictures",
            ZoomStepPreset.Large,
            Autostart: true,
            new HotkeySpec(HotkeyKind.Chord, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x41),
            new HotkeySpec(HotkeyKind.Combo, HotkeyModifiers.Shift, 0x50));

        SettingsStore.Save(path, original);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "easysniplite-tests-missing-" + Guid.NewGuid().ToString("N") + ".json");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(new EasySnipLite.Core.Settings.Settings(), loaded);
        Assert.Equal(HotkeySpec.DefaultScreenshot, loaded.ResolvedScreenshotHotkey);
        Assert.Equal(HotkeySpec.DefaultPassthrough, loaded.ResolvedPassthroughHotkey);
        Assert.Equal(1.1, loaded.ZoomFactor);
    }

    [Fact]
    public void GarbageJson_ReturnsDefaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{{{{ not json }}}}");

        Assert.Equal(new EasySnipLite.Core.Settings.Settings(), SettingsStore.Load(path));
    }

    [Fact]
    public void UnknownEnumValue_NormalizesToDefaults()
    {
        var path = TempPath();
        File.WriteAllText(path, """{"Language":99,"ZoomStep":7}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(AppLanguage.System, loaded.Language);
        Assert.Equal(ZoomStepPreset.Medium, loaded.ZoomStep);
    }

    [Fact]
    public void InvalidHotkeySpec_NormalizesToNull()
    {
        var path = TempPath();
        File.WriteAllText(path, """{"ScreenshotHotkey":{"Kind":0,"Modifiers":1,"VirtualKey":0}}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(HotkeySpec.DefaultScreenshot, loaded.ResolvedScreenshotHotkey);
    }

    [Fact]
    public void ZoomFactor_MapsPresets()
    {
        Assert.Equal(1.05, new EasySnipLite.Core.Settings.Settings(ZoomStep: ZoomStepPreset.Small).ZoomFactor);
        Assert.Equal(1.1, new EasySnipLite.Core.Settings.Settings(ZoomStep: ZoomStepPreset.Medium).ZoomFactor);
        Assert.Equal(1.2, new EasySnipLite.Core.Settings.Settings(ZoomStep: ZoomStepPreset.Large).ZoomFactor);
    }

    [Fact]
    public void ConflictsWith_RejectsSameKeyAndModifiers()
    {
        var a = new HotkeySpec(HotkeyKind.Chord, HotkeyModifiers.Ctrl, 0x41);
        var b = new HotkeySpec(HotkeyKind.Combo, HotkeyModifiers.Ctrl, 0x41);
        var c = new HotkeySpec(HotkeyKind.Combo, HotkeyModifiers.Ctrl, 0x50);

        Assert.True(a.ConflictsWith(b));
        Assert.False(a.ConflictsWith(c));
    }
}
