using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EasySnipLite.Core.Imaging;

namespace EasySnipLite.Selection;

/// <summary>
/// 单台显示器的全屏透明遮罩窗口：显示冻结帧、暗化遮罩与选区。
/// 交互事件转发给 SelectionSession（会话层统一虚拟物理坐标）。
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private readonly MonitorCapture _frame;
    private readonly SelectionSession _session;

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

        Cursor = Cursors.Cross;
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            _session.OnLeftButtonDown(this, e.GetPosition(RootCanvas));
        };
        MouseLeftButtonUp += (_, _) => _session.OnLeftButtonUp();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>窗口内逻辑坐标 → 虚拟屏幕物理坐标。</summary>
    public Point LocalToVirtual(Point localPos) =>
        new(_frame.PixelX + localPos.X * _frame.DpiScale,
            _frame.PixelY + localPos.Y * _frame.DpiScale);

    /// <summary>会话广播选区（虚拟物理坐标，可跨屏）→ 渲染本窗口内的部分。</summary>
    public void UpdateSelection(Int32Rect? selection)
    {
        if (selection is null || selection.Value.IsEmpty)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            UpdateMasks(null);
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
            UpdateMasks(windowRect);
            return;
        }

        SelectionRect.Visibility = Visibility.Visible;
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

        UpdateMasks(local);
    }

    private void UpdateMasks(Rect? selection)
    {
        var windowRect = new Rect(0, 0, Width, Height);
        var sel = selection ?? Rect.Empty;
        PositionMask(MaskTop, new Rect(0, 0, windowRect.Width, Math.Max(0, sel.Top)));
        PositionMask(MaskBottom, new Rect(0, sel.Bottom, windowRect.Width, Math.Max(0, windowRect.Bottom - sel.Bottom)));
        PositionMask(MaskLeft, new Rect(0, sel.Top, Math.Max(0, sel.Left), Math.Max(0, sel.Height)));
        PositionMask(MaskRight, new Rect(sel.Right, sel.Top, Math.Max(0, windowRect.Right - sel.Right), Math.Max(0, sel.Height)));
    }

    private static void PositionMask(Rectangle mask, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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
