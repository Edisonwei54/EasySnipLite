using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.ClipboardServices;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Native;

namespace EasySnipLite.Pin;

/// <summary>
/// 贴屏窗口：无边框置顶，初始 1:1（物理像素/DpiScale）显示在截图原位置。
/// 拖动=左键 DragMove；Ctrl+滚轮缩放（50%~300%）；右键菜单：穿透/透明度/100% 缩放/复制/保存/关闭。
/// 穿透=SetWindowLongPtr 增删 WS_EX_TRANSPARENT（WPF AllowsTransparency 窗口已是 layered，透明度走 Window.Opacity）。
/// 多张并存：App 持有列表管理，Topmost 组内点击激活即置前。
/// </summary>
public partial class PinWindow : Window
{
    private readonly BitmapSource _image; // 物理像素原图（复制/保存/重算布局用）
    private readonly int _pixelX;
    private readonly int _pixelY;
    private double _dpiScale = 1.0;
    private double _zoom = 1.0;
    private bool _passthrough;

    public PinWindow(BitmapSource image, int pixelX, int pixelY)
    {
        InitializeComponent();
        _image = image;
        _pixelX = pixelX;
        _pixelY = pixelY;
        PinImage.Source = image;
        Loaded += OnLoaded;
    }

    public bool IsPassthrough
    {
        get => _passthrough;
        set
        {
            if (_passthrough == value) return;
            _passthrough = value;
            ApplyPassthrough();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        ApplyLayout();
    }

    /// <summary>窗口尺寸/位置 = 物理像素 / DpiScale（×zoom）。</summary>
    private void ApplyLayout()
    {
        var (w, h) = PinMath.LayoutSize(_image.PixelWidth, _image.PixelHeight, _dpiScale, _zoom);
        var (x, y) = PinMath.LayoutPosition(_pixelX, _pixelY, _dpiScale);
        Width = w;
        Height = h;
        Left = x;
        Top = y;
    }

    /// <summary>切换 WS_EX_TRANSPARENT（穿透）。SWP_FRAMECHANGED 强制刷新扩展样式生效。</summary>
    private void ApplyPassthrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        style = _passthrough ? style | Win32.WS_EX_TRANSPARENT : style & ~Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED);
    }

    // ---- 交互 ----

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        _zoom = PinMath.NextZoom(_zoom, e.Delta > 0);
        ApplyLayout();
        e.Handled = true;
    }

    // ---- 右键菜单 ----

    private void Passthrough_Toggled(object sender, RoutedEventArgs e)
    {
        // IsChecked 是 bool?，用 bool 类型模式解包（null 不匹配）
        if (sender is System.Windows.Controls.MenuItem { IsChecked: bool isChecked })
            IsPassthrough = isChecked;
    }

    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag } && double.TryParse(tag, out var alpha))
            Opacity = alpha;
    }

    private void Zoom100_Click(object sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        ApplyLayout();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClipboardEx.SetImage(_image);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"复制失败：{ex.Message}", "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImageFile.SavePngWithDialog(_image, ImageFile.DefaultFileName());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
