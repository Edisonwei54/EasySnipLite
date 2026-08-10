namespace EasySnipLite.Core.Settings;

public enum AppLanguage
{
    System,
    English,
    SimplifiedChinese,
    TraditionalChinese,
}

public enum ZoomStepPreset
{
    Small = 5,   // 5%
    Medium = 10, // 10%
    Large = 20,  // 20%
}

public sealed record Settings(
    AppLanguage Language = AppLanguage.System,
    string? SaveDirectory = null,
    ZoomStepPreset ZoomStep = ZoomStepPreset.Medium,
    bool Autostart = false,
    HotkeySpec? ScreenshotHotkey = null,   // null = 默认 Ctrl+双击Space
    HotkeySpec? PassthroughHotkey = null)  // null = 默认 Ctrl+Shift+P
{
    public HotkeySpec ResolvedScreenshotHotkey =>
        ScreenshotHotkey ?? HotkeySpec.DefaultScreenshot;
    public HotkeySpec ResolvedPassthroughHotkey =>
        PassthroughHotkey ?? HotkeySpec.DefaultPassthrough;

    /// <summary>贴屏滚轮缩放每格步长因子（5/10/20% → ×1.05/×1.1/×1.2）。</summary>
    public double ZoomFactor => ZoomStep switch
    {
        ZoomStepPreset.Small => 1.05,
        ZoomStepPreset.Large => 1.2,
        _ => 1.1,
    };

    /// <summary>校验并规范化（未知枚举/非法 spec 回退默认），Load 时调用。</summary>
    public Settings Normalize() => new(
        Enum.IsDefined(Language) ? Language : AppLanguage.System,
        SaveDirectory,
        Enum.IsDefined(ZoomStep) ? ZoomStep : ZoomStepPreset.Medium,
        Autostart,
        ValidSpec(ScreenshotHotkey),
        ValidSpec(PassthroughHotkey));

    private static HotkeySpec? ValidSpec(HotkeySpec? spec) =>
        spec is { VirtualKey: > 0 } && Enum.IsDefined(spec.Kind) ? spec : null;
}
