using EasySnipLite.Core.Native;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>
/// 热键录制（纯逻辑）：喂入键盘事件流。
/// Chord 模式：按住修饰键 + 双击目标键；Combo 模式：按一次组合键。
/// autoDetect 模式（截图热键）：单击/双击自动识别——双击窗口内第二次同键→Chord，
/// 窗口到期未再按→由 HandleTimeout 把候选敲定为 Combo。
/// Esc 随时取消。结果经 Recorded/Cancelled 事件输出。
/// 修饰键状态取事件自身携带的标志（CtrlDown/ShiftDown/AltDown）。
/// </summary>
public sealed class HotkeyRecorder
{
    private readonly HotkeyKind _mode;
    private readonly TimeSpan _doubleTapWindow;
    private readonly bool _autoDetect;
    private int _firstKey;
    private bool _hasFirst;
    private DateTime _firstRelease;
    private HotkeyModifiers _firstMods;

    public HotkeyRecorder(HotkeyKind mode, TimeSpan doubleTapWindow, bool autoDetect = false)
    {
        _mode = mode;
        _doubleTapWindow = doubleTapWindow;
        _autoDetect = autoDetect;
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
            if (e.Type != KeyEventType.KeyDown || ModifierKey.IsModifier(e.VirtualKey)) return;
            Recorded?.Invoke(new HotkeySpec(HotkeyKind.Combo, ModsOf(e), e.VirtualKey));
            return;
        }

        // Chord 模式：只看非修饰键 KeyUp
        if (e.Type != KeyEventType.KeyUp || ModifierKey.IsModifier(e.VirtualKey)) return;
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

    /// <summary>自动识别模式：单击候选在双击窗口到期后敲定为 Combo（由钩子线程定时器调用，与 HandleKey 同线程）。</summary>
    public void HandleTimeout(DateTime now)
    {
        if (!_autoDetect || !_hasFirst) return;
        if (now - _firstRelease >= _doubleTapWindow)
        {
            Recorded?.Invoke(new HotkeySpec(HotkeyKind.Combo, _firstMods, _firstKey));
            _hasFirst = false;
        }
    }

    private static HotkeyModifiers ModsOf(KeyEvent e)
    {
        var m = HotkeyModifiers.None;
        if (e.CtrlDown) m |= HotkeyModifiers.Ctrl;
        if (e.ShiftDown) m |= HotkeyModifiers.Shift;
        if (e.AltDown) m |= HotkeyModifiers.Alt;
        return m;
    }
}
