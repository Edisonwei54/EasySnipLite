using System.Runtime.InteropServices;
using EasySnipLite.Core.Native;

namespace EasySnipLite.Stitching;

/// <summary>
/// M4 长截图滚轮模拟:SendInput 在指定屏幕坐标施放滚轮事件。
/// 滚轮增量正数=向上滚,负数=向下滚(看页面下方内容)。
/// </summary>
public static class ScrollInput
{
    /// <summary>标准滚轮一格的增量(120)。</summary>
    public const int WheelDelta = 120;

    /// <summary>在屏幕物理像素坐标处滚动 notches 格(正数向下滚,负数向上滚)。</summary>
    public static void ScrollDownAt(int screenX, int screenY, int notches)
    {
        if (notches == 0) return;
        Win32.SetCursorPos(screenX, screenY);
        var input = new Win32.INPUT
        {
            type = Win32.INPUT_MOUSE,
            mi = new Win32.MOUSEINPUT
            {
                mouseData = unchecked((uint)(-notches * WheelDelta)), // 负增量 = 向下滚
                dwFlags = Win32.MOUSEEVENTF_WHEEL,
            },
        };
        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
    }
}
