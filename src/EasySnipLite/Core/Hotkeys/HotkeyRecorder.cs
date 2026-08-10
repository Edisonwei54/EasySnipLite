using EasySnipLite.Core.Native;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>
/// 热键录制（纯逻辑）：喂入键盘事件流。
/// Chord 模式：按住修饰键 + 双击目标键；Combo 模式：按一次组合键。
/// Esc 随时取消。结果经 Recorded/Cancelled 事件输出。
/// 修饰键状态取事件自身携带的标志（CtrlDown/ShiftDown/AltDown）。
/// </summary>
public sealed class HotkeyRecorder
{
    private readonly HotkeyKind _mode;
    private readonly TimeSpan _doubleTapWindow;
    private int _firstKey;
    private bool _hasFirst;
    private DateTime _firstRelease;
    private HotkeyModifiers _firstMods;

    public HotkeyRecorder(HotkeyKind mode, TimeSpan doubleTapWindow)
    {
        _mode = mode;
        _doubleTapWindow = doubleTapWindow;
    }

    public event Action<HotkeySpec>? Recorded;
    public event Action? Cancelled;

    public void HandleKey(KeyEvent e)
    {
        if (e.VirtualKey == Win32.VK_ESCAPE && e.Type == KeyEventType.KeyDown)
        {
            Cancelled?.Invoke();
            return;
        }

        if (_mode == HotkeyKind.Combo)
        {
            if (e.Type != KeyEventType.KeyDown || IsModifierKey(e.VirtualKey)) return;
            Recorded?.Invoke(new HotkeySpec(HotkeyKind.Combo, ModsOf(e), e.VirtualKey));
            return;
        }

        // Chord 模式：只看非修饰键 KeyUp
        if (e.Type != KeyEventType.KeyUp || IsModifierKey(e.VirtualKey)) return;
        var mods = ModsOf(e);
        if (!_hasFirst)
        {
            _firstKey = e.VirtualKey;
            _firstRelease = e.Timestamp;
            _firstMods = mods;
            _hasFirst = true;
            return;
        }
        if (e.VirtualKey == _firstKey
            && e.Timestamp - _firstRelease <= _doubleTapWindow
            && mods == _firstMods)
        {
            Recorded?.Invoke(new HotkeySpec(HotkeyKind.Chord, mods, e.VirtualKey));
            _hasFirst = false; // 复位，可连续录制
            return;
        }
        // 不同键 / 超窗 / 修饰键变化 → 以本次为新的候选起点（last-wins）
        _firstKey = e.VirtualKey;
        _firstRelease = e.Timestamp;
        _firstMods = mods;
    }

    private static HotkeyModifiers ModsOf(KeyEvent e)
    {
        var m = HotkeyModifiers.None;
        if (e.CtrlDown) m |= HotkeyModifiers.Ctrl;
        if (e.ShiftDown) m |= HotkeyModifiers.Shift;
        if (e.AltDown) m |= HotkeyModifiers.Alt;
        return m;
    }

    private static bool IsModifierKey(int vk) =>
        vk is Win32.VK_CONTROL or Win32.VK_SHIFT or Win32.VK_MENU;
}
