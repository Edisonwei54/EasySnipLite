using EasySnipLite.Core.Settings;

namespace EasySnipLite.Core.Hotkeys;

/// <summary>单键组合热键判定（纯逻辑）：修饰键精确匹配 + 每次物理按压只触发一次（防 auto-repeat）。</summary>
public sealed class ComboDetector
{
    private readonly int _targetKey;
    private readonly HotkeyModifiers _modifiers;
    private bool _armed;

    public ComboDetector(int targetKey, HotkeyModifiers modifiers)
    {
        _targetKey = targetKey;
        _modifiers = modifiers;
    }

    /// <summary>KeyDown 且 vk 匹配、修饰键精确匹配、本次按压未触发过 → true；KeyUp 复位。</summary>
    public bool HandleKey(KeyEvent e)
    {
        if (e.VirtualKey != _targetKey) return false;
        if (e.Type == KeyEventType.KeyUp)
        {
            _armed = false;
            return false;
        }
        if (_armed) return false; // 按住时的重复 KeyDown（auto-repeat）
        if (!ModifierMatch.IsMatch(_modifiers, e)) return false;
        _armed = true;
        return true;
    }
}
