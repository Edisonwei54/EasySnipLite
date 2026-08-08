using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using EasySnipLite.Core.Native;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>
/// WH_KEYBOARD_LL 低级键盘钩子。钩子回调运行在专用 STA 线程的消息循环上
/// （低级钩子的回调必须所在线程有消息循环，否则会被系统静默卸载）。
/// 注意：回调内严禁任何慢操作（磁盘 I/O 等），否则会被系统超时移除。
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private IntPtr _hook;
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private Win32.LowLevelKeyboardProc? _callback; // 持有引用防止被 GC

    /// <summary>钩子线程上触发（非 UI 线程），需要 UI 交互时请转发。</summary>
    public event Action<KeyEvent>? KeyReceived;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "EasySnipLite.KeyboardHook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void ThreadMain()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = Callback;
        _hook = Win32.SetWindowsHookEx(
            Win32.WH_KEYBOARD_LL, _callback, Win32.GetModuleHandle(null), 0);
        Dispatcher.Run();
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is Win32.WM_KEYDOWN or Win32.WM_KEYUP or Win32.WM_SYSKEYDOWN or Win32.WM_SYSKEYUP)
            {
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                bool ctrlDown = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
                bool shiftDown = (Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0;
                bool isUp = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
                var evt = new KeyEvent(
                    isUp ? KeyEventType.KeyUp : KeyEventType.KeyDown,
                    (int)data.vkCode,
                    ctrlDown,
                    shiftDown,
                    DateTime.UtcNow);
                KeyReceived?.Invoke(evt);
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_dispatcher is not null)
        {
            _dispatcher.InvokeAsync(() =>
            {
                if (_hook != IntPtr.Zero)
                {
                    Win32.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            });
        }
        _thread?.Join(1000);
        _thread = null;
    }
}
