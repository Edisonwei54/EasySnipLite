using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.ClipboardServices;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Editor.Models;
using EasySnipLite.Localization;

namespace EasySnipLite.Editor;

/// <summary>表情面板数据源（ItemsControl 绑定用）。</summary>
public sealed class EmojiPalette
{
    public string[] Smile => EmojiCatalog.Categories[0];
    public string[] Gesture => EmojiCatalog.Categories[1];
    public string[] Animal => EmojiCatalog.Categories[2];
    public string[] Food => EmojiCatalog.Categories[3];
    public string[] Object => EmojiCatalog.Categories[4];
}

/// <summary>
/// 标注编辑器窗口：画布（底图+矢量对象+选中装饰）+ 工具条（9 工具/颜色/粗细/撤销/重做/删除）
/// + 动作条（复制/保存/贴到屏幕/完成）。快捷键：Delete 删除、Ctrl+Z/Y 撤销重做、Ctrl+C/S 复制保存、
/// Enter 完成（复制并关闭）、Esc 关闭。
/// </summary>
public partial class EditorWindow : Window
{
    private readonly EditorViewModel _vm;
    private readonly ToggleButton[] _toolButtons;
    private Point _emojiPoint;

    /// <summary>「贴到屏幕」：携带组合位图，由 App 打开贴屏窗口（本窗口随即关闭）。</summary>
    public event Action<BitmapSource>? PinRequested;

    public EditorWindow(BitmapSource image)
    {
        InitializeComponent();

        _vm = new EditorViewModel(image);
        Canvas.Image = image;
        Canvas.Objects = _vm.Objects;
        _vm.RenderInvalidated += () => Canvas.InvalidateVisual();
        _vm.TextInputRequested += ShowTextInput;
        _vm.EmojiInputRequested += ShowEmojiPanel;
        EmojiPopup.DataContext = new EmojiPalette();

        StrokeWidthCombo.ItemsSource = new[] { "1", "2", "3", "5", "8" };
        StrokeWidthCombo.SelectedItem = "3";

        _toolButtons =
        [
            BtnSelection, BtnRect, BtnEllipse, BtnArrow, BtnFreehand,
            BtnHighlighter, BtnMosaic, BtnText, BtnEmoji,
        ];

        Localize();
    }

    /// <summary>从资源应用全部本地化字符串（模态窗口，构造时调用一次）。</summary>
    private void Localize()
    {
        Title = AppResources.EditorTitle;
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
        TabSmile.Header = AppResources.EmojiCategorySmile;
        TabGesture.Header = AppResources.EmojiCategoryGesture;
        TabAnimal.Header = AppResources.EmojiCategoryAnimal;
        TabFood.Header = AppResources.EmojiCategoryFood;
        TabObject.Header = AppResources.EmojiCategoryObject;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 画布布局尺寸 = 物理像素 / DpiScale
        var dpi = VisualTreeHelper.GetDpi(this);
        Canvas.Width = _vm.Image.PixelWidth / dpi.DpiScaleX;
        Canvas.Height = _vm.Image.PixelHeight / dpi.DpiScaleY;
        BtnSelection.IsChecked = true;
    }

    // ---- 画布交互 ----

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        Canvas.CaptureMouse();
        _vm.OnMouseDown(e.GetPosition(Canvas));
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        _vm.OnMouseMove(e.GetPosition(Canvas));
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _vm.OnMouseUp(e.GetPosition(Canvas));
        Canvas.ReleaseMouseCapture();
    }

    // ---- 工具条 ----

    private void ToolButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag }) return;
        if (!Enum.TryParse<AnnotationTool>(tag, out var tool)) return;
        SetTool(tool, (ToggleButton)sender);
    }

    /// <summary>切换激活工具并同步按钮选中态（按钮点击与数字键共用）。</summary>
    private void SetTool(AnnotationTool tool, ToggleButton? source = null)
    {
        _vm.ActiveTool = tool;
        // ToggleButton 无 GroupName，手动互斥
        foreach (var btn in _toolButtons)
        {
            btn.IsChecked = !ReferenceEquals(btn, source) && (AnnotationTool)Enum.Parse<AnnotationTool>((string)btn.Tag!) == tool;
        }
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Background: SolidColorBrush brush })
            _vm.StrokeColor = brush.Color;
    }

    private void StrokeWidth_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StrokeWidthCombo.SelectedItem is string s && double.TryParse(s, out var width))
            _vm.StrokeWidth = width;
    }

    // ---- 编辑操作 ----

    private void Undo_Click(object sender, RoutedEventArgs e) => _vm.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _vm.Redo();
    private void Delete_Click(object sender, RoutedEventArgs e) => _vm.DeleteSelected();

    // ---- 动作条 ----

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImageFile.SavePngWithDialog(_vm.Compose(), ImageFile.DefaultFileName());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(AppResources.SaveFailed, ex.Message), "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Complete_Click(object sender, RoutedEventArgs e) => Complete();

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PinRequested?.Invoke(_vm.Compose());
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(AppResources.PinFailed, ex.Message), "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---- 输入面板 ----

    private void ShowTextInput(Point p)
    {
        var dialog = new TextInputDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            _vm.CommitText(p, dialog.Text);
    }

    private void ShowEmojiPanel(Point p)
    {
        _emojiPoint = p;
        EmojiPopup.IsOpen = true;
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string emoji })
            _vm.CommitEmoji(_emojiPoint, emoji);
        EmojiPopup.IsOpen = false;
    }

    // ---- 快捷键 ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.D1: SetTool(AnnotationTool.Selection); e.Handled = true; break;
            case Key.D2: SetTool(AnnotationTool.Rectangle); e.Handled = true; break;
            case Key.D3: SetTool(AnnotationTool.Ellipse); e.Handled = true; break;
            case Key.D4: SetTool(AnnotationTool.Arrow); e.Handled = true; break;
            case Key.D5: SetTool(AnnotationTool.Freehand); e.Handled = true; break;
            case Key.D6: SetTool(AnnotationTool.Highlighter); e.Handled = true; break;
            case Key.D7: SetTool(AnnotationTool.Mosaic); e.Handled = true; break;
            case Key.D8: SetTool(AnnotationTool.Text); e.Handled = true; break;
            case Key.D9: SetTool(AnnotationTool.Emoji); e.Handled = true; break;

            case Key.Delete:
                _vm.DeleteSelected();
                e.Handled = true;
                break;
            case Key.Z when ctrl && !shift:
                _vm.Undo();
                e.Handled = true;
                break;
            case Key.Y when ctrl:
            case Key.Z when ctrl && shift:
                _vm.Redo();
                e.Handled = true;
                break;
            case Key.C when ctrl:
                CopyToClipboard();
                e.Handled = true;
                break;
            case Key.S when ctrl:
                Save_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Enter:
                Complete();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void CopyToClipboard()
    {
        try
        {
            ClipboardEx.SetImage(_vm.Compose());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(AppResources.CopyFailed, ex.Message), "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Complete()
    {
        CopyToClipboard();
        Close();
    }
}
