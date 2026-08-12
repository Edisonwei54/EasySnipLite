using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasySnipLite.Editor.Models;
using EasySnipLite.Localization;

namespace EasySnipLite.Selection;

/// <summary>
/// 悬浮标注工具栏（issue #20）：框选完成后出现在选区下方的无边框置顶窗口。
/// 点击不抢键盘焦点（Focusable=False）；事件由 SelectionSession 转发到 EditorViewModel。
/// Reposition 由会话在选区变化时调用，定位规则见 SelectionMath.ToolbarPlacement。
/// </summary>
public partial class AnnotationToolbarWindow : Window
{
    private readonly ToggleButton[] _toolButtons;
    private (Int32Rect region, Int32Rect bounds, double dpi)? _pending;

    public event Action<AnnotationTool>? ToolSelected;
    public event Action<Color>? ColorSelected;
    public event Action<double>? StrokeWidthChanged;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? DeleteRequested;
    public event Action? CopyRequested;
    public event Action? SaveRequested;
    public event Action? PinRequested;
    public event Action? CompleteRequested;

    public AnnotationToolbarWindow()
    {
        InitializeComponent();

        StrokeWidthCombo.ItemsSource = new[] { "1", "2", "3", "5", "8" };
        StrokeWidthCombo.SelectedItem = "3";

        _toolButtons =
        [
            BtnSelection, BtnRect, BtnEllipse, BtnArrow, BtnFreehand,
            BtnHighlighter, BtnMosaic, BtnText, BtnEmoji,
        ];

        Loaded += (_, _) =>
        {
            if (_pending is { } p) ApplyPosition(p);
        };

        Localize();
    }

    /// <summary>按当前选区与虚拟屏幕重定位（SizeToContent 完成后 ActualWidth 才有效）。</summary>
    public void Reposition(Int32Rect region, Int32Rect bounds, double dpi)
    {
        _pending = (region, bounds, dpi);
        if (ActualWidth > 0 && ActualHeight > 0) ApplyPosition(_pending.Value);
    }

    private void ApplyPosition((Int32Rect region, Int32Rect bounds, double dpi) p)
    {
        var topLeft = SelectionMath.ToolbarPlacement(p.region, p.bounds, new Size(ActualWidth, ActualHeight), p.dpi);
        Left = topLeft.X / p.dpi;
        Top = topLeft.Y / p.dpi;
    }

    /// <summary>会话同步激活工具（数字键/外部切换时按钮态跟随）。</summary>
    public void SetTool(AnnotationTool tool)
    {
        foreach (var btn in _toolButtons)
        {
            btn.IsChecked = (AnnotationTool)Enum.Parse<AnnotationTool>((string)btn.Tag!) == tool;
        }
    }

    private void Localize()
    {
        BtnSelection.Content = AppResources.ToolSelection;
        BtnRect.Content = AppResources.ToolRectangle;
        BtnEllipse.Content = AppResources.ToolEllipse;
        BtnArrow.Content = AppResources.ToolArrow;
        BtnFreehand.Content = AppResources.ToolFreehand;
        BtnHighlighter.Content = AppResources.ToolHighlighter;
        BtnMosaic.Content = AppResources.ToolMosaic;
        BtnText.Content = AppResources.ToolText;
        BtnEmoji.Content = AppResources.ToolEmoji;
        ColorRed.ToolTip = AppResources.ColorRed;
        ColorOrange.ToolTip = AppResources.ColorOrange;
        ColorYellow.ToolTip = AppResources.ColorYellow;
        ColorGreen.ToolTip = AppResources.ColorGreen;
        ColorBlue.ToolTip = AppResources.ColorBlue;
        ColorPurple.ToolTip = AppResources.ColorPurple;
        ColorBlack.ToolTip = AppResources.ColorBlack;
        ColorWhite.ToolTip = AppResources.ColorWhite;
        StrokeWidthCombo.ToolTip = AppResources.StrokeWidth;
        BtnUndo.Content = AppResources.Undo;
        BtnRedo.Content = AppResources.Redo;
        BtnDelete.Content = AppResources.Delete;
        BtnCopy.Content = AppResources.ActionCopy;
        BtnSave.Content = AppResources.ActionSave;
        BtnPin.Content = AppResources.PinToScreen;
        BtnComplete.Content = AppResources.Complete;
        BtnComplete.ToolTip = AppResources.TtComplete;
    }

    // ---- 工具条 ----

    private void ToolButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag }) return;
        if (!Enum.TryParse<AnnotationTool>(tag, out var tool)) return;
        // ToggleButton 无 GroupName，手动互斥
        foreach (var btn in _toolButtons)
        {
            btn.IsChecked = !ReferenceEquals(btn, sender) && (AnnotationTool)Enum.Parse<AnnotationTool>((string)btn.Tag!) == tool;
        }
        ToolSelected?.Invoke(tool);
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Background: SolidColorBrush brush })
            ColorSelected?.Invoke(brush.Color);
    }

    private void StrokeWidth_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StrokeWidthCombo.SelectedItem is string s && double.TryParse(s, out var width))
            StrokeWidthChanged?.Invoke(width);
    }

    // ---- 编辑操作 ----

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();
    private void Redo_Click(object sender, RoutedEventArgs e) => RedoRequested?.Invoke();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();

    // ---- 动作 ----

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke();
    private void Pin_Click(object sender, RoutedEventArgs e) => PinRequested?.Invoke();
    private void Complete_Click(object sender, RoutedEventArgs e) => CompleteRequested?.Invoke();
}
