using System.Resources;

namespace EasySnipLite.Localization;

/// <summary>强类型资源访问器（resx 三语：中性=英文，zh-Hans/zh-Hant 卫星程序集自动回退英文）。</summary>
public static class AppResources
{
    private static readonly ResourceManager Manager =
        new("EasySnipLite.Localization.AppResources", typeof(AppResources).Assembly);

    // 托盘
    public static string TrayCaptureFormat => Get("TrayCaptureFormat");
    public static string TraySettings => Get("TraySettings");
    public static string TrayExit => Get("TrayExit");
    // 贴屏右键菜单
    public static string PinPassthrough => Get("PinPassthrough");
    public static string PinOpacity => Get("PinOpacity");
    public static string PinZoom100 => Get("PinZoom100");
    public static string PinCopy => Get("PinCopy");
    public static string PinSave => Get("PinSave");
    public static string PinClose => Get("PinClose");
    // 编辑器
    public static string EditorTitle => Get("EditorTitle");
    public static string ToolSelection => Get("ToolSelection");
    public static string ToolRectangle => Get("ToolRectangle");
    public static string ToolEllipse => Get("ToolEllipse");
    public static string ToolArrow => Get("ToolArrow");
    public static string ToolFreehand => Get("ToolFreehand");
    public static string ToolHighlighter => Get("ToolHighlighter");
    public static string ToolMosaic => Get("ToolMosaic");
    public static string ToolText => Get("ToolText");
    public static string ToolEmoji => Get("ToolEmoji");
    public static string ColorRed => Get("ColorRed");
    public static string ColorOrange => Get("ColorOrange");
    public static string ColorYellow => Get("ColorYellow");
    public static string ColorGreen => Get("ColorGreen");
    public static string ColorBlue => Get("ColorBlue");
    public static string ColorPurple => Get("ColorPurple");
    public static string ColorBlack => Get("ColorBlack");
    public static string ColorWhite => Get("ColorWhite");
    public static string StrokeWidth => Get("StrokeWidth");
    public static string Undo => Get("Undo");
    public static string Redo => Get("Redo");
    public static string Delete => Get("Delete");
    public static string ActionCopy => Get("ActionCopy");
    public static string ActionSave => Get("ActionSave");
    public static string PinToScreen => Get("PinToScreen");
    public static string Complete => Get("Complete");
    public static string TtComplete => Get("TtComplete");
    public static string EmojiCategorySmile => Get("EmojiCategorySmile");
    public static string EmojiCategoryGesture => Get("EmojiCategoryGesture");
    public static string EmojiCategoryAnimal => Get("EmojiCategoryAnimal");
    public static string EmojiCategoryFood => Get("EmojiCategoryFood");
    public static string EmojiCategoryObject => Get("EmojiCategoryObject");
    // 文字输入框
    public static string TextInputTitle => Get("TextInputTitle");
    public static string Ok => Get("Ok");
    public static string Cancel => Get("Cancel");
    // 消息框
    public static string SingleInstanceMsg => Get("SingleInstanceMsg");
    public static string CopyFailed => Get("CopyFailed");
    public static string SaveFailed => Get("SaveFailed");
    public static string PinFailed => Get("PinFailed");
    // 文件过滤器
    public static string PngFilter => Get("PngFilter");
    // 热键录制
    public static string HotkeyDoubleTap => Get("HotkeyDoubleTap");
    public static string RecordHotkey => Get("RecordHotkey");
    public static string RecordPrompt => Get("RecordPrompt");
    public static string RecordConflict => Get("RecordConflict");
    public static string RecordApply => Get("RecordApply");
    public static string RecordRetry => Get("RecordRetry");
    // 设置窗口
    public static string SettingsTitle => Get("SettingsTitle");
    public static string GeneralSection => Get("GeneralSection");
    public static string LanguageLabel => Get("LanguageLabel");
    public static string LangFollowSystem => Get("LangFollowSystem");
    public static string SaveDirLabel => Get("SaveDirLabel");
    public static string Browse => Get("Browse");
    public static string ZoomStepLabel => Get("ZoomStepLabel");
    public static string ZoomSmall => Get("ZoomSmall");
    public static string ZoomMedium => Get("ZoomMedium");
    public static string ZoomLarge => Get("ZoomLarge");
    public static string AutostartLabel => Get("AutostartLabel");
    public static string HotkeySection => Get("HotkeySection");
    public static string CaptureHotkeyLabel => Get("CaptureHotkeyLabel");
    public static string PassthroughHotkeyLabel => Get("PassthroughHotkeyLabel");
    public static string SaveBtn => Get("SaveBtn");
    public static string CancelBtn => Get("CancelBtn");
    public static string ResetBtn => Get("ResetBtn");

    // 错误与启动提示（M7）
    public static string AppStarted => Get("AppStarted");
    public static string SettingsSaveFailed => Get("SettingsSaveFailed");
    public static string UnhandledNotify => Get("UnhandledNotify");
    public static string UnhandledErrorBody => Get("UnhandledErrorBody");

    private static string Get(string key) => Manager.GetString(key) ?? key;
}
