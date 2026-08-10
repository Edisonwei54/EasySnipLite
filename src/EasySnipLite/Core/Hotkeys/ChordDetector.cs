using EasySnipLite.Core.Native;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>
/// 判定「修饰键按住 + 目标键双击」的时序逻辑（纯逻辑，可单测）。
/// 修饰键精确匹配：声明的必须按下，未声明的必须未按下。
/// </summary>
public sealed class ChordDetector
{
    private readonly TimeSpan _doubleTapWindow;
    private readonly int _targetKey;
    private readonly HotkeyModifiers _modifiers;
    private DateTime _lastRelease;
    private bool _hasLastRelease;

    public ChordDetector(
        TimeSpan doubleTapWindow,
        int targetKey = Win32.VK_SPACE,
        HotkeyModifiers modifiers = HotkeyModifiers.Ctrl)
    {
        _doubleTapWindow = doubleTapWindow;
        _targetKey = targetKey;
        _modifiers = modifiers;
    }

    /// <summary>喂入一个键盘事件；当满足双击条件时返回 true（触发后自动重置，避免连击重复触发）。</summary>
    public bool HandleKey(KeyEvent e)
    {
        if (e.Type != KeyEventType.KeyUp || e.VirtualKey != _targetKey)
        {
            return false;
        }
        if (!ModifierMatch.IsMatch(_modifiers, e))
        {
            return false; // 修饰键不匹配不记时，避免污染双击时序
        }

        bool fired = _hasLastRelease
                     && e.Timestamp - _lastRelease <= _doubleTapWindow;
        if (fired)
        {
            _hasLastRelease = false; // 重置，防止三连击重复触发
            return true;
        }

        _lastRelease = e.Timestamp;
        _hasLastRelease = true;
        return false;
    }
}
