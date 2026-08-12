using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Editor.Models;
using EasySnipLite.Localization;

namespace EasySnipLite.Selection;

/// <summary>
/// 单台显示器的全屏透明遮罩窗口：显示冻结帧、暗化遮罩、选区、8 手柄、尺寸标签与角点放大镜。
/// 交互事件转发给 SelectionSession（会话层统一虚拟物理坐标）。
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private const double HandleSize = 9;        // 手柄边长(逻辑像素),与 XAML 样式一致
    private const int MagnifierCells = 9;       // 放大镜网格(物理像素)
    private const int MagnifierHalf = 4;
    private const double MagnifierCellSize = 6; // 每物理像素放大到的逻辑像素
    private static readonly Size MagnifierSize = new(58, 76);

    private static readonly System.Windows.Media.Brush SelectionFill = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));

    private readonly MonitorCapture _frame;
    private readonly SelectionSession _session;
    private readonly WriteableBitmap _magBitmap;
    private readonly Rectangle[] _handles;
    private bool _showHandles;
    private Point _emojiPoint; // 表情面板点击处的选区本地坐标（提交用）

    public RegionSelectionWindow(MonitorCapture frame, SelectionSession session)
    {
        _frame = frame;
        _session = session;
        InitializeComponent();

        Left = frame.PixelX / frame.DpiScale;
        Top = frame.PixelY / frame.DpiScale;
        Width = frame.PixelWidth / frame.DpiScale;
        Height = frame.PixelHeight / frame.DpiScale;

        FrozenImage.Source = frame.Image;
        FrozenImage.Width = frame.PixelWidth / frame.DpiScale;
        FrozenImage.Height = frame.PixelHeight / frame.DpiScale;
        Canvas.SetLeft(FrozenImage, 0);
        Canvas.SetTop(FrozenImage, 0);

        _magBitmap = new WriteableBitmap(MagnifierCells, MagnifierCells, 96, 96, PixelFormats.Bgra32, null);
        MagImage.Source = _magBitmap;
        _handles = new[]
        {
            HandleNW, HandleNE, HandleSW, HandleSE,
            HandleTop, HandleBottom, HandleLeft, HandleRight,
        };

        TabSmile.Header = AppResources.EmojiCategorySmile;
        TabGesture.Header = AppResources.EmojiCategoryGesture;
        TabAnimal.Header = AppResources.EmojiCategoryAnimal;
        TabFood.Header = AppResources.EmojiCategoryFood;
        TabObject.Header = AppResources.EmojiCategoryObject;

        Cursor = Cursors.Cross;
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            CaptureMouse(); // 捕获:拖拽在工具栏/其他窗口上释放也能收到 MouseUp(坐标仍走全局轮询)
            _session.OnLeftButtonDown(this, e.GetPosition(RootCanvas),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        };
        MouseLeftButtonUp += (_, e) =>
        {
            _session.OnLeftButtonUp(this, e.GetPosition(RootCanvas));
            ReleaseMouseCapture();
        };
        MouseMove += (_, e) => _session.OnHover(this, e.GetPosition(RootCanvas),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        MouseLeave += (_, _) => Cursor = Cursors.Cross;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>本显示器 DpiScale（工具栏定位换算用）。</summary>
    public double DpiScale => _frame.DpiScale;

    /// <summary>窗口内逻辑坐标 → 虚拟屏幕物理坐标。</summary>
    public Point LocalToVirtual(Point localPos) =>
        new(_frame.PixelX + localPos.X * _frame.DpiScale,
            _frame.PixelY + localPos.Y * _frame.DpiScale);

    /// <summary>虚拟物理坐标是否落在本窗口覆盖的显示器内(跨屏时定位放大镜用)。</summary>
    public bool ContainsVirtual(Point p) =>
        p.X >= _frame.PixelX && p.X < _frame.PixelX + _frame.PixelWidth &&
        p.Y >= _frame.PixelY && p.Y < _frame.PixelY + _frame.PixelHeight;

    /// <summary>会话广播选区（虚拟物理坐标，可跨屏）→ 渲染本窗口内的部分。</summary>
    public void UpdateSelection(Int32Rect? selection, bool showHandles)
    {
        _showHandles = showHandles;
        if (selection is null || selection.Value.IsEmpty)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            HideHandles();
            UpdateMasks(null);
            AnnotationLayer.Visibility = Visibility.Collapsed;
            return;
        }

        var s = selection.Value;
        // 转窗口内逻辑坐标
        double dpi = _frame.DpiScale;
        var local = new Rect(
            (s.X - _frame.PixelX) / dpi,
            (s.Y - _frame.PixelY) / dpi,
            s.Width / dpi,
            s.Height / dpi);

        // 与本窗口可视区域裁剪
        var windowRect = new Rect(0, 0, Width, Height);
        local.Intersect(windowRect);

        if (local.IsEmpty)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            HideHandles();
            UpdateMasks(windowRect);
            return;
        }

        SelectionRect.Visibility = Visibility.Visible;
        // 标注模式:选区已冻结为图像,半透明白色填充只用于框选阶段,避免盖住标注
        SelectionRect.Fill = _session.IsAnnotationActive ? Brushes.Transparent : SelectionFill;
        Canvas.SetLeft(SelectionRect, local.X);
        Canvas.SetTop(SelectionRect, local.Y);
        SelectionRect.Width = local.Width;
        SelectionRect.Height = local.Height;

        // 尺寸标签显示完整选区尺寸（物理像素）
        SizeText.Text = $"{s.Width} × {s.Height}";
        SizeLabel.Visibility = Visibility.Visible;
        SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelX = local.Right + 4;
        double labelY = local.Top - SizeLabel.DesiredSize.Height - 4;
        if (labelY < 0) labelY = local.Bottom + 4;
        if (labelX + SizeLabel.DesiredSize.Width > Width) labelX = local.Left - SizeLabel.DesiredSize.Width - 4;
        Canvas.SetLeft(SizeLabel, labelX);
        Canvas.SetTop(SizeLabel, labelY);

        UpdateHandles(local);
        UpdateMasks(local);
        UpdateAnnotationLayer(s, dpi);
    }

    /// <summary>内联标注层（issue #20）：标注激活时在选区位置显示冻结图像 + 矢量对象，按 DpiScale 缩放到 1:1。</summary>
    private void UpdateAnnotationLayer(Int32Rect selection, double dpi)
    {
        if (!_session.IsAnnotationActive || _session.AnnotationVm is not { } vm)
        {
            AnnotationLayer.Visibility = Visibility.Collapsed;
            return;
        }

        AnnotationLayer.Visibility = Visibility.Visible;
        AnnotationLayer.Image = _session.AnnotationImage;
        AnnotationLayer.Objects = vm.Objects;
        AnnotationLayer.Width = selection.Width;   // 物理像素单位，LayoutTransform 缩放到窗口逻辑尺寸
        AnnotationLayer.Height = selection.Height;
        Canvas.SetLeft(AnnotationLayer, (selection.X - _frame.PixelX) / dpi);
        Canvas.SetTop(AnnotationLayer, (selection.Y - _frame.PixelY) / dpi);
        AnnotationLayer.RenderTransform = new ScaleTransform(1.0 / dpi, 1.0 / dpi);
        AnnotationLayer.InvalidateVisual();
    }

    /// <summary>标注对象/选中变化时重绘标注层。</summary>
    public void InvalidateAnnotationLayer() => AnnotationLayer.InvalidateVisual();

    /// <summary>显示表情选择面板（标注模式表情工具点击后调用，点击处即鼠标位置）。</summary>
    public void ShowEmojiPanel(Point regionLocalPos)
    {
        _emojiPoint = regionLocalPos;
        EmojiPopup.DataContext ??= new Editor.EmojiPalette();
        EmojiPopup.IsOpen = true;
    }

    public void SetCursorForHandle(SelectionHandle handle)
    {
        Cursor = handle switch
        {
            SelectionHandle.CornerNW or SelectionHandle.CornerSE => Cursors.SizeNWSE,
            SelectionHandle.CornerNE or SelectionHandle.CornerSW => Cursors.SizeNESW,
            SelectionHandle.EdgeTop or SelectionHandle.EdgeBottom => Cursors.SizeNS,
            SelectionHandle.EdgeLeft or SelectionHandle.EdgeRight => Cursors.SizeWE,
            SelectionHandle.Body => Cursors.SizeAll,
            _ => Cursors.Cross,
        };
    }

    // ---- 放大镜 ----

    public void ShowMagnifier() => Magnifier.Visibility = Visibility.Visible;

    public void HideMagnifier() => Magnifier.Visibility = Visibility.Collapsed;

    /// <summary>更新放大镜:中心 9x9 物理像素最近邻放大 + 坐标 + 中心颜色,定位在鼠标右下(自动避让)。</summary>
    public void UpdateMagnifier(Point cursorVirtual)
    {
        int cx = (int)Math.Round(cursorVirtual.X);
        int cy = (int)Math.Round(cursorVirtual.Y);
        int fx = cx - _frame.PixelX, fy = cy - _frame.PixelY;
        var area = new Int32Rect(
            Math.Max(0, fx - MagnifierHalf),
            Math.Max(0, fy - MagnifierHalf),
            Math.Min(_frame.PixelWidth, fx + MagnifierHalf + 1) - Math.Max(0, fx - MagnifierHalf),
            Math.Min(_frame.PixelHeight, fy + MagnifierHalf + 1) - Math.Max(0, fy - MagnifierHalf));
        if (area.Width <= 0 || area.Height <= 0) return;

        var crop = new CroppedBitmap(_frame.Image, area);
        var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var px = new byte[area.Width * area.Height * 4];
        bgra.CopyPixels(px, area.Width * 4, 0);

        // 铺到 9x9 位图,越界格填暗色
        var buf = new byte[MagnifierCells * MagnifierCells * 4];
        for (int i = 0; i < buf.Length; i += 4)
        {
            buf[i] = 40; buf[i + 1] = 40; buf[i + 2] = 40; buf[i + 3] = 255;
        }
        int srcX = Math.Max(0, MagnifierHalf - fx);
        int srcY = Math.Max(0, MagnifierHalf - fy);
        for (int row = 0; row < area.Height; row++)
        {
            Buffer.BlockCopy(px, row * area.Width * 4, buf, ((srcY + row) * MagnifierCells + srcX) * 4, area.Width * 4);
        }
        _magBitmap.WritePixels(new Int32Rect(0, 0, MagnifierCells, MagnifierCells), buf, MagnifierCells * 4, 0);

        // 中心像素颜色
        int c = (MagnifierHalf * MagnifierCells + MagnifierHalf) * 4;
        var color = Color.FromArgb(buf[c + 3], buf[c + 2], buf[c + 1], buf[c]);
        MagInfo.Text = $"({cx}, {cy})  #{color.R:X2}{color.G:X2}{color.B:X2}";

        // 定位:鼠标逻辑坐标 → 右下偏移,自动避让屏幕边缘
        double dpi = _frame.DpiScale;
        var mouseLogical = new Point(
            (cursorVirtual.X - _frame.PixelX) / dpi,
            (cursorVirtual.Y - _frame.PixelY) / dpi);
        var pos = SelectionMath.MagnifierTopLeft(mouseLogical, new Size(Width, Height), MagnifierSize);
        Canvas.SetLeft(Magnifier, pos.X);
        Canvas.SetTop(Magnifier, pos.Y);
    }

    // ---- 内部渲染 ----

    private void UpdateHandles(Rect local)
    {
        if (!_showHandles)
        {
            HideHandles();
            return;
        }
        PositionHandle(HandleNW, local.Left, local.Top);
        PositionHandle(HandleNE, local.Right, local.Top);
        PositionHandle(HandleSW, local.Left, local.Bottom);
        PositionHandle(HandleSE, local.Right, local.Bottom);
        PositionHandle(HandleTop, local.Left + local.Width / 2, local.Top);
        PositionHandle(HandleBottom, local.Left + local.Width / 2, local.Bottom);
        PositionHandle(HandleLeft, local.Left, local.Top + local.Height / 2);
        PositionHandle(HandleRight, local.Right, local.Top + local.Height / 2);
    }

    private void PositionHandle(Rectangle handle, double x, double y)
    {
        if (x < 0 || y < 0 || x > Width || y > Height)
        {
            handle.Visibility = Visibility.Collapsed;
            return;
        }
        handle.Visibility = Visibility.Visible;
        Canvas.SetLeft(handle, x - HandleSize / 2);
        Canvas.SetTop(handle, y - HandleSize / 2);
    }

    private void HideHandles()
    {
        foreach (var handle in _handles)
        {
            handle.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateMasks(Rect? selection)
    {
        var windowRect = new Rect(0, 0, Width, Height);
        var (top, bottom, left, right) = SelectionMath.MaskRectangles(windowRect, selection);
        PositionMask(MaskTop, top);
        PositionMask(MaskBottom, bottom);
        PositionMask(MaskLeft, left);
        PositionMask(MaskRight, right);
    }

    private static void PositionMask(Rectangle mask, Rect rect)
    {
        // 防御:NaN/Inf 尺寸会抛 ArgumentException(见 Rect.Empty 的 -Inf 坑)
        bool invalid = double.IsNaN(rect.Width) || double.IsNaN(rect.Height)
                       || double.IsPositiveInfinity(rect.Width) || double.IsPositiveInfinity(rect.Height);
        if (invalid || rect.Width <= 0 || rect.Height <= 0)
        {
            mask.Visibility = Visibility.Collapsed;
            return;
        }
        mask.Visibility = Visibility.Visible;
        Canvas.SetLeft(mask, rect.X);
        Canvas.SetTop(mask, rect.Y);
        mask.Width = rect.Width;
        mask.Height = rect.Height;
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string emoji })
            _session.CommitEmoji(_emojiPoint, emoji);
        EmojiPopup.IsOpen = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            int dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            _session.OnNudge(dx, dy, shift);
            return;
        }
        // 标注模式快捷键（issue #20）：数字切工具 / 删除 / 撤销重做 / 复制保存
        if (_session.IsAnnotationActive)
        {
            switch (e.Key)
            {
                case Key.D1: _session.SetAnnotationTool(AnnotationTool.Selection); e.Handled = true; return;
                case Key.D2: _session.SetAnnotationTool(AnnotationTool.Rectangle); e.Handled = true; return;
                case Key.D3: _session.SetAnnotationTool(AnnotationTool.Ellipse); e.Handled = true; return;
                case Key.D4: _session.SetAnnotationTool(AnnotationTool.Arrow); e.Handled = true; return;
                case Key.D5: _session.SetAnnotationTool(AnnotationTool.Freehand); e.Handled = true; return;
                case Key.D6: _session.SetAnnotationTool(AnnotationTool.Highlighter); e.Handled = true; return;
                case Key.D7: _session.SetAnnotationTool(AnnotationTool.Mosaic); e.Handled = true; return;
                case Key.D8: _session.SetAnnotationTool(AnnotationTool.Text); e.Handled = true; return;
                case Key.D9: _session.SetAnnotationTool(AnnotationTool.Emoji); e.Handled = true; return;
                case Key.Delete: _session.DeleteSelected(); e.Handled = true; return;
            }
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (e.Key == Key.Z && ctrl && !shift) { _session.UndoAnnotation(); e.Handled = true; return; }
            if (e.Key == Key.Y && ctrl || e.Key == Key.Z && ctrl && shift) { _session.RedoAnnotation(); e.Handled = true; return; }
            if (e.Key == Key.C && ctrl) { _session.CopyToClipboard(); e.Handled = true; return; }
        }
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            _session.OnSave();
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                _session.OnCancel();
                break;
            case Key.Enter:
                e.Handled = true;
                _session.OnConfirm();
                break;
        }
    }
}
