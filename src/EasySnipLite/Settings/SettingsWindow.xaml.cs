using System.Windows;
using System.Windows.Controls;
using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Settings;
using EasySnipLite.Localization;

// 命名空间 EasySnipLite.Settings 与类型 EasySnipLite.Core.Settings.Settings 同名：
// 简单名 Settings 在文件作用域 using 下会绑到 enclosing 命名空间（成员优先于 using），导致 CS0118；
// 故改用块作用域命名空间并把别名放进命名空间体内（最内层成员优先，别名胜出）。同 CLAUDE.md 规则 6 的命名冲突。
namespace EasySnipLite.Settings
{
    using Settings = EasySnipLite.Core.Settings.Settings;

/// <summary>
/// 设置窗口（托盘「设置」入口，模态）。常规区（语言/保存目录/滚轮步长/自启）+ 快捷键区（录制）。
/// 语言下拉变化仅窗口内预览（不广播）；点「保存」→ apply 回调（App 落盘并全局应用）。
/// 录制：按钮变提示文案 → App 录制入口（自身热键屏蔽）→ 冲突校验 → 应用或重试。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Func<HotkeyKind, Task<HotkeySpec?>> _record;
    private readonly Action<Settings> _apply;
    private Settings _draft;
    private bool _suppressPreview; // 重建下拉项时抑制 SelectionChanged 再入
    // 待确认的录制结果：捕获成功后按钮变「确定」，点击才写入 _draft；再次点本行「录制」=重试覆盖
    private (HotkeyKind Kind, HotkeySpec Spec, Button Btn, TextBlock Display)? _pending;

    public SettingsWindow(Settings current, Func<HotkeyKind, Task<HotkeySpec?>> record, Action<Settings> apply)
    {
        InitializeComponent();
        _draft = current;
        _record = record;
        _apply = apply;
        Localize();
    }

    private void Localize()
    {
        _suppressPreview = true;

        Title = AppResources.SettingsTitle;
        GeneralHeader.Text = AppResources.GeneralSection;
        LanguageLabel.Text = AppResources.LanguageLabel;
        SaveDirLabel.Text = AppResources.SaveDirLabel;
        BrowseBtn.Content = AppResources.Browse;
        ZoomStepLabel.Text = AppResources.ZoomStepLabel;
        AutostartLabel.Text = AppResources.AutostartLabel;
        HotkeyHeader.Text = AppResources.HotkeySection;
        CaptureLabel.Text = AppResources.CaptureHotkeyLabel;
        PassthroughLabel.Text = AppResources.PassthroughHotkeyLabel;
        CaptureRecordBtn.Content = AppResources.RecordHotkey;
        PassthroughRecordBtn.Content = AppResources.RecordHotkey;
        SaveBtn.Content = AppResources.SaveBtn;
        CancelBtn.Content = AppResources.CancelBtn;
        ResetBtn.Content = AppResources.ResetBtn;

        // 语言下拉：跟随系统 + 三种语言（语言名用母语，不本地化）
        var lang = _draft.Language;
        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(AppResources.LangFollowSystem); // index 0 = System
        LanguageCombo.Items.Add("English");                    // 1
        LanguageCombo.Items.Add("简体中文");                     // 2
        LanguageCombo.Items.Add("繁體中文");                     // 3
        LanguageCombo.SelectedIndex = (int)lang;

        var zoom = _draft.ZoomStep;
        ZoomCombo.Items.Clear();
        ZoomCombo.Items.Add(AppResources.ZoomSmall);   // 0 = Small
        ZoomCombo.Items.Add(AppResources.ZoomMedium);  // 1 = Medium
        ZoomCombo.Items.Add(AppResources.ZoomLarge);   // 2 = Large
        ZoomCombo.SelectedIndex = zoom switch
        {
            ZoomStepPreset.Small => 0,
            ZoomStepPreset.Large => 2,
            _ => 1,
        };

        SaveDirText.Text = _draft.SaveDirectory ?? "";
        AutostartCheck.IsChecked = _draft.Autostart;
        CaptureHotkeyText.Text = HotkeyFormat.Format(_draft.ResolvedScreenshotHotkey, AppResources.HotkeyDoubleTap);
        PassthroughHotkeyText.Text = HotkeyFormat.Format(_draft.ResolvedPassthroughHotkey, AppResources.HotkeyDoubleTap);

        _suppressPreview = false;
    }

    // ---- 常规区 ----

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPreview || LanguageCombo.SelectedIndex < 0) return;
        _draft = _draft with { Language = (AppLanguage)LanguageCombo.SelectedIndex };
        Localize(); // 仅窗口内预览（下拉重建被 _suppressPreview 挡住再入）
    }

    private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPreview || ZoomCombo.SelectedIndex < 0) return;
        _draft = _draft with { ZoomStep = ZoomIndex(ZoomCombo.SelectedIndex) };
    }

    private void Autostart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPreview) return;
        _draft = _draft with { Autostart = AutostartCheck.IsChecked == true };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _draft = _draft with { SaveDirectory = dialog.SelectedPath };
            SaveDirText.Text = dialog.SelectedPath;
        }
    }

    // ---- 快捷键录制（两阶段：录制 → 捕获/冲突校验 → 确定或重试）----

    private async void RecordCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is { } p)
        {
            if (p.Btn == CaptureRecordBtn) CommitPending(p); // 本行待确认 → 确定
            return;                                          // 其他行待确认 → 忽略
        }
        await RecordAsync(HotkeyKind.Chord, CaptureRecordBtn, CaptureHotkeyText, _draft.ResolvedPassthroughHotkey);
    }

    private async void RecordPassthrough_Click(object sender, RoutedEventArgs e)
    {
        if (_pending is { } p)
        {
            if (p.Btn == PassthroughRecordBtn) CommitPending(p);
            return;
        }
        await RecordAsync(HotkeyKind.Combo, PassthroughRecordBtn, PassthroughHotkeyText, _draft.ResolvedScreenshotHotkey);
    }

    /// <summary>录制：按钮变提示 → App 录制（自身热键屏蔽）→ 取消/冲突恢复可重试；成功进入待确认。</summary>
    private async Task RecordAsync(HotkeyKind kind, Button btn, TextBlock display, HotkeySpec other)
    {
        CaptureRecordBtn.IsEnabled = false;
        PassthroughRecordBtn.IsEnabled = false;
        btn.Content = AppResources.RecordPrompt;

        var spec = await _record(kind);
        btn.Content = AppResources.RecordHotkey;
        CaptureRecordBtn.IsEnabled = true;
        PassthroughRecordBtn.IsEnabled = true;
        if (spec is null) return; // Esc 取消：按钮恢复「录制」，可重试

        if (spec.ConflictsWith(other))
        {
            MessageBox.Show(this, AppResources.RecordConflict, "EasySnipLite",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return; // 按钮已恢复「录制」，再次点击即重试
        }

        // 成功：显示新键，按钮变「确定」等待确认
        display.Text = HotkeyFormat.Format(spec, AppResources.HotkeyDoubleTap);
        _pending = (kind, spec, btn, display);
        btn.Content = AppResources.RecordApply;
    }

    /// <summary>确认：把待确认结果写入 _draft，按钮恢复「录制」。</summary>
    private void CommitPending((HotkeyKind Kind, HotkeySpec Spec, Button Btn, TextBlock Display) p)
    {
        _draft = p.Kind == HotkeyKind.Chord
            ? _draft with { ScreenshotHotkey = p.Spec }
            : _draft with { PassthroughHotkey = p.Spec };
        p.Btn.Content = AppResources.RecordHotkey;
        _pending = null;
    }

    // ---- 操作 ----

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _apply(_draft);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _draft = new Settings();
        Localize();
    }

    private static ZoomStepPreset ZoomIndex(int index) =>
        index switch
        {
            0 => ZoomStepPreset.Small,
            2 => ZoomStepPreset.Large,
            _ => ZoomStepPreset.Medium,
        };
}
}
