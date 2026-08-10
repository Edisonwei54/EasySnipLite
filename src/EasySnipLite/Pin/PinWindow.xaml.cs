using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.ClipboardServices;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Native;
using EasySnipLite.Localization;

namespace EasySnipLite.Pin;

/// <summary>
/// 贴屏窗口：无边框置顶，初始 1:1（物理像素/DpiScale）显示在截图原位置。
/// 拖动=左键 DragMove；Ctrl+滚轮缩放（50%~300%，步长可配）；右键菜单：穿透/透明度/100% 缩放/复制/保存/关闭
/// （代码动态构建，语言切换时 ApplyLocale 重建并刷新步长）。
/// 穿透=SetWindowLongPtr 增删 WS_EX_TRANSPARENT（WPF AllowsTransparency 窗口已是 layered，透明度走 Window.Opacity）。
/// </summary>
public partial class PinWindow : Window
{
    private readonly BitmapSource _image; // 物理像素原图（复制/保存/重算布局用）
    private readonly int _pixelX;
    private readonly int _pixelY;
    private double _dpiScale = 1.0;
    private double _zoom = 1.0;
    private double _zoomStep = PinMath.ZoomStep;
    private bool _passthrough;
    private System.Windows.Controls.MenuItem? _passthroughMenuItem;

    public PinWindow(BitmapSource image, int pixelX, int pixelY, double zoomStep = PinMath.ZoomStep)
    {
        InitializeComponent();
        _image = image;
        _pixelX = pixelX;
        _pixelY = pixelY;
        _zoomStep = zoomStep;
        PinImage.Source = image;
        Loaded += OnLoaded;
        // 跨屏拖动到不同 DPI 显示器时刷新缩放比例（DpiChanged 在窗口句柄存在后触发，WPF 保证）
        DpiChanged += (_, _) =>
        {
            _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            ApplyLayout();
        };
        Localize();
    }

    /// <summary>语言切换/设置变更时由 App 调用：刷新步长并重建右键菜单。</summary>
    public void ApplyLocale(double zoomStep)
    {
        _zoomStep = zoomStep;
        Localize();
    }

    public bool IsPassthrough
    {
        get => _passthrough;
        set
        {
            if (_passthrough == value) return;
            _passthrough = value;
            if (_passthroughMenuItem is not null)
                _passthroughMenuItem.IsChecked = _passthrough; // 勾选态同步(Checked 事件再入被早退挡,无递归)
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
        _zoom = PinMath.NextZoom(_zoom, e.Delta > 0, _zoomStep);
        ApplyLayout();
        e.Handled = true;
    }

    // ---- 右键菜单（代码构建，支持语言即时切换）----

    private void Localize()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        _passthroughMenuItem = new System.Windows.Controls.MenuItem
        {
            Header = AppResources.PinPassthrough,
            IsCheckable = true,
            IsChecked = _passthrough,
        };
        _passthroughMenuItem.Checked += Passthrough_Toggled;
        _passthroughMenuItem.Unchecked += Passthrough_Toggled;

        var opacity = new System.Windows.Controls.MenuItem { Header = AppResources.PinOpacity };
        foreach (var (label, tag) in new[] { ("100%", "1.0"), ("85%", "0.85"), ("70%", "0.7"), ("50%", "0.5") })
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, Tag = tag };
            item.Click += Opacity_Click;
            opacity.Items.Add(item);
        }

        var zoom100 = new System.Windows.Controls.MenuItem { Header = AppResources.PinZoom100 };
        zoom100.Click += Zoom100_Click;
        var copy = new System.Windows.Controls.MenuItem { Header = AppResources.PinCopy };
        copy.Click += Copy_Click;
        var save = new System.Windows.Controls.MenuItem { Header = AppResources.PinSave };
        save.Click += Save_Click;
        var close = new System.Windows.Controls.MenuItem { Header = AppResources.PinClose };
        close.Click += Close_Click;

        menu.Items.Add(_passthroughMenuItem);
        menu.Items.Add(opacity);
        menu.Items.Add(zoom100);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(copy);
        menu.Items.Add(save);
        menu.Items.Add(close);
        ContextMenu = menu;
    }

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
            MessageBox.Show(this, string.Format(AppResources.CopyFailed, ex.Message), "EasySnipLite",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, string.Format(AppResources.SaveFailed, ex.Message), "EasySnipLite",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
