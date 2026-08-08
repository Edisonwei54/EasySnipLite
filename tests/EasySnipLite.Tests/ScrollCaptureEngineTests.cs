using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using EasySnipLite.Stitching;

namespace EasySnipLite.Tests;

/// <summary>
/// M4 滚动捕获引擎调度循环:滚动→稳定→截帧→对齐→拼接;到底检测 / 对齐失败重试 / 高度上限 / 取消。
/// 截帧与滚动均注入假实现,帧序列模拟真实滚动后的内容。
/// </summary>
public class ScrollCaptureEngineTests
{
    private const int W = 48, H = 36;

    // ---- 合成帧辅助 ----

    /// <summary>高噪声内容图(BGRA),帧 = 内容在 scrollPos 处的窗口裁剪。</summary>
    private static byte[] MakeContent(int contentH, int seed)
    {
        var rnd = new Random(seed);
        var px = new byte[W * contentH * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            byte v = (byte)rnd.Next(256);
            px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
        }
        return px;
    }

    /// <summary>从内容图 scrollPos 处裁剪一帧;超出内容底部的行填背景色。</summary>
    private static Bitmap MakeFrame(byte[] content, int contentH, int scrollPos, byte bg = 0)
    {
        var pixels = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            int srcY = scrollPos + y;
            for (int x = 0; x < W; x++)
            {
                int dstIdx = (y * W + x) * 4;
                if (srcY < contentH)
                {
                    Array.Copy(content, (srcY * W + x) * 4, pixels, dstIdx, 4);
                }
                else
                {
                    pixels[dstIdx] = bg; pixels[dstIdx + 1] = bg; pixels[dstIdx + 2] = bg; pixels[dstIdx + 3] = 255;
                }
            }
        }
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    /// <summary>引擎参数:零延时快速跑,不真正滚动。</summary>
    private static ScrollCaptureOptions FastOptions(Action<ScrollCaptureOptions>? tweak = null)
    {
        var o = new ScrollCaptureOptions
        {
            StabilizeDelayMs = 0,
            WheelNotches = 0,
            RetryAttempts = 2,
            BottomConfirmCount = 2,
            MaxHeight = 20000,
        };
        tweak?.Invoke(o);
        return o;
    }

    // ---- 拼接 ----

    [Fact]
    public async Task Run_ThreeFrames_StitchesToExpectedHeight()
    {
        var content = MakeContent(400, 1);
        var frames = new Queue<Bitmap>(
        [
            MakeFrame(content, 400, 0),
            MakeFrame(content, 400, 30),
            MakeFrame(content, 400, 60),
            MakeFrame(content, 400, 60), // 到底:连续内容未变
            MakeFrame(content, 400, 60),
        ]);
        var engine = new ScrollCaptureEngine(() => frames.Dequeue(), () => { }, FastOptions());

        var result = await engine.RunAsync(CancellationToken.None);

        Assert.True(result.ReachedBottom);
        Assert.False(result.Cancelled);
        Assert.Null(result.Error);
        Assert.Equal(3, result.FrameCount);
        Assert.Equal(H + (H - 30) + (H - 30), result.Height); // 36 + 6 + 6 = 48
        Assert.NotNull(result.Image);
        Assert.Empty(result.FailedSeams);

        // 抽查拼接正确性:canvas 行 y 对应 content 的哪一行,按帧贡献映射
        // 帧1 整帧 content[0..36) 画到 [0..36);帧2 底部新行 content[60..66) 画到 [36..42);
        // 帧3 底部新行 content[90..96) 画到 [42..48)
        int[] startCanvas = [0, H, H + (H - 30)];
        int[] startContent = [0, 60, 90]; // 新露出的内容行 = 已滚动行数累计
        int contentRow(int y)
        {
            int f = y < H ? 0 : (y < H + (H - 30) ? 1 : 2);
            return startContent[f] + (y - startCanvas[f]);
        }

        using (result.Image!)
        {
            var data = result.Image.LockBits(new Rectangle(0, 0, W, result.Image.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var canvasBytes = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, canvasBytes, 0, canvasBytes.Length);
                for (int y = 0; y < result.Image.Height; y++)
                {
                    int srcY = contentRow(y);
                    for (int x = 0; x < W; x += 5)
                    {
                        int dstIdx = y * data.Stride + x * 4;
                        int srcIdx = (srcY * W + x) * 4;
                        Assert.True(content.AsSpan(srcIdx, 4).SequenceEqual(
                            canvasBytes.AsSpan(dstIdx, 4)),
                            $"行 {y} 列 {x} 像素不匹配");
                    }
                }
            }
            finally
            {
                result.Image.UnlockBits(data);
            }
        }
    }

    // ---- 到底检测 ----

    [Fact]
    public async Task Run_ReachesBottom_StopsWithExpectedHeight()
    {
        // 内容 90 行:pos0 → pos30 → pos60(超出底部填背景) → 再滚内容不变 → 连续 2 次判底
        var content = MakeContent(90, 2);
        var frames = new Queue<Bitmap>(
        [
            MakeFrame(content, 90, 0, 200),
            MakeFrame(content, 90, 30, 200),
            MakeFrame(content, 90, 60, 200),
            MakeFrame(content, 90, 60, 200),
            MakeFrame(content, 90, 60, 200),
        ]);
        var engine = new ScrollCaptureEngine(() => frames.Dequeue(), () => { }, FastOptions());

        var result = await engine.RunAsync(CancellationToken.None);

        Assert.True(result.ReachedBottom);
        Assert.Equal(3, result.FrameCount); // 仅拼接帧计数
        Assert.Equal(H + (H - 30) + (H - 30), result.Height); // 36 + 6 + 6 = 48
    }

    // ---- 对齐失败重试 ----

    [Fact]
    public async Task Run_AlignFail_RetriesThenSucceeds()
    {
        // pos0 → pos30 → 坏帧(动画跳变) → 稳定后重截 pos30 → pos60
        var content = MakeContent(400, 3);
        var bad = MakeContent(400, 99); // 完全无关内容
        var frames = new Queue<Bitmap>(
        [
            MakeFrame(content, 400, 0),
            MakeFrame(content, 400, 30),
            MakeFrame(bad, 400, 30),
            MakeFrame(content, 400, 30),
            MakeFrame(content, 400, 60),
            MakeFrame(content, 400, 60), // 到底
            MakeFrame(content, 400, 60),
        ]);
        var engine = new ScrollCaptureEngine(() => frames.Dequeue(), () => { }, FastOptions());

        var result = await engine.RunAsync(CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(3, result.FrameCount);
        Assert.Equal(H + (H - 30) + (H - 30), result.Height);
        Assert.Empty(result.FailedSeams);
    }

    [Fact]
    public async Task Run_AlignFailExceedsRetries_StopsWithSeam()
    {
        var content = MakeContent(400, 4);
        var bad = MakeContent(400, 98);
        var frames = new Queue<Bitmap>(
        [
            MakeFrame(content, 400, 0),
            MakeFrame(content, 400, 30),
            MakeFrame(bad, 400, 30),
            MakeFrame(bad, 400, 30),
            MakeFrame(bad, 400, 30), // 第 3 次仍失败 > RetryAttempts=2
        ]);
        var engine = new ScrollCaptureEngine(() => frames.Dequeue(), () => { }, FastOptions());

        var result = await engine.RunAsync(CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Single(result.FailedSeams);
        Assert.Equal(H + (H - 30), result.Height); // 36 + 6 = 42,坏帧未被拼接
    }

    // ---- 上限 / 取消 / 异常 ----

    [Fact]
    public async Task Run_HeightLimit_Stops()
    {
        var content = MakeContent(400, 5);
        var frames = new Queue<Bitmap>(
        [
            MakeFrame(content, 400, 0),
            MakeFrame(content, 400, 30),
            MakeFrame(content, 400, 60),
            MakeFrame(content, 400, 90),
        ]);
        var engine = new ScrollCaptureEngine(() => frames.Dequeue(), () => { },
            FastOptions(o => o.MaxHeight = 50));

        var result = await engine.RunAsync(CancellationToken.None);

        Assert.True(result.HeightLimitReached);
        Assert.True(result.Height >= 50); // 36 → 42 → 48 → 54,第 3 次拼接后触发
    }

    [Fact]
    public async Task Run_Cancelled_Stops()
    {
        var content = MakeContent(400, 6);
        var frames = new Queue<Bitmap>(
        [
            MakeFrame(content, 400, 0),
            MakeFrame(content, 400, 30),
            MakeFrame(content, 400, 60),
            MakeFrame(content, 400, 90),
            MakeFrame(content, 400, 120),
        ]);
        using var cts = new CancellationTokenSource();
        var engine = new ScrollCaptureEngine(() =>
        {
            if (frames.Count == 3) cts.Cancel(); // 第 3 次截帧时请求取消(捕获中途)
            return frames.Dequeue();
        }, () => { }, FastOptions());

        var result = await engine.RunAsync(cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(3, result.FrameCount); // 帧1 首帧 + 帧2 拼接 + 帧3 拼接后检查取消
    }

    [Fact]
    public async Task Run_CaptureReturnsNull_ReportsError()
    {
        var content = MakeContent(400, 7);
        var frames = new Queue<Bitmap?>([MakeFrame(content, 400, 0), null]);
        var engine = new ScrollCaptureEngine(() => frames.Dequeue()!, () => { }, FastOptions());

        var result = await engine.RunAsync(CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Equal(1, result.FrameCount);
    }
}
