namespace EasySnipLite.Core.Hotkeys;

public enum KeyEventType
{
    KeyDown,
    KeyUp,
}

public sealed record KeyEvent(KeyEventType Type, int VirtualKey, bool CtrlDown, DateTime Timestamp);

/// <summary>
/// 判定「修饰键按住 + 目标键双击」的时序逻辑（纯逻辑，可单测）。
/// </summary>
public sealed class ChordDetector
{
    private const int DefaultTargetKey = 0x20; // VK_SPACE
    private readonly TimeSpan _doubleTapWindow;
    private readonly int _targetKey;
    private DateTime _lastRelease;
    private bool _hasLastRelease;

    public ChordDetector(TimeSpan doubleTapWindow, int targetKey = DefaultTargetKey)
    {
        _doubleTapWindow = doubleTapWindow;
        _targetKey = targetKey;
    }

    /// <summary>喂入一个键盘事件；当满足双击条件时返回 true（触发后自动重置，避免连击重复触发）。</summary>
    public bool HandleKey(KeyEvent e)
    {
        if (e.Type != KeyEventType.KeyUp || e.VirtualKey != _targetKey)
        {
            return false;
        }

        bool fired = e.CtrlDown && _hasLastRelease
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
