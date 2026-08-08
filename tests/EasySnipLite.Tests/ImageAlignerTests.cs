using EasySnipLite.Stitching;

namespace EasySnipLite.Tests;

/// <summary>
/// M4 长截图帧对齐纯逻辑:灰度降采样 + 按行 SAD 垂直偏移搜索。
/// 合成图场景:已知偏移恢复 / 无重叠返回 null / 噪声鲁棒 / 滚动条干扰忽略。
/// </summary>
public class ImageAlignerTests
{
    // ---- 合成图辅助 ----

    private static byte[] MakeNoise(int w, int h, int seed, int bpp = 4)
    {
        var rnd = new Random(seed);
        var px = new byte[w * h * bpp];
        for (int i = 0; i < px.Length; i += bpp)
        {
            byte v = (byte)rnd.Next(256);
            for (int c = 0; c < bpp; c++) px[i + c] = v;
        }
        return px;
    }

    /// <summary>低对比度渐变(灰度,含少量抖动),模拟真实页面难匹配的平滑区域。</summary>
    private static byte[] MakeGradient(int w, int h, int seed, int bpp = 4)
    {
        var rnd = new Random(seed);
        var px = new byte[w * h * bpp];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte v = (byte)(x * 255 / w / 3 + y * 255 / h / 3 + rnd.Next(3));
            int i = (y * w + x) * bpp;
            for (int c = 0; c < bpp; c++) px[i + c] = v;
        }
        return px;
    }

    /// <summary>
    /// 模拟页面向下滚动 offset 行后的新帧:内容整体上移(new[y] = old[y+offset]),
    /// 顶部露出新内容,底部滚出旧内容、出现页面新内容。
    /// </summary>
    private static byte[] ScrollDown(byte[] src, int w, int h, int offset, int seed, int bpp = 4)
    {
        var rnd = new Random(seed);
        var dst = new byte[src.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * bpp;
            if (y < h - offset)
            {
                Array.Copy(src, ((y + offset) * w + x) * bpp, dst, i, bpp);
            }
            else
            {
                byte v = (byte)rnd.Next(256);
                for (int c = 0; c < bpp; c++) dst[i + c] = v;
            }
        }
        return dst;
    }

    /// <summary>3x 均匀均值近似正态,σ 由 sigma 控制;只扰动前三个颜色通道(灰度通道同噪)。</summary>
    private static void AddNoise(byte[] px, int seed, int sigma, int bpp = 4)
    {
        var rnd = new Random(seed);
        for (int i = 0; i < px.Length; i += bpp)
        {
            for (int c = 0; c < Math.Min(3, bpp); c++)
            {
                double n = (rnd.NextDouble() + rnd.NextDouble() + rnd.NextDouble() - 1.5) / 1.5;
                px[i + c] = (byte)Math.Clamp(px[i + c] + (int)Math.Round(n * sigma), 0, 255);
            }
        }
    }

    // ---- 偏移恢复 ----

    [Fact]
    public void FindVerticalOffset_NoiseShift_ReturnsExactOffset()
    {
        const int w = 200, h = 120, offset = 37;
        var oldFrame = MakeNoise(w, h, 11);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 22);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    [Fact]
    public void FindVerticalOffset_GradientShift_ReturnsExactOffset()
    {
        const int w = 240, h = 160, offset = 25;
        var oldFrame = MakeGradient(w, h, 5);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 6);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    [Fact]
    public void FindVerticalOffset_SmallShift_ReturnsExactOffset()
    {
        const int w = 160, h = 100;
        for (int offset = 1; offset <= 3; offset++)
        {
            var oldFrame = MakeNoise(w, h, 31 + offset);
            var newFrame = ScrollDown(oldFrame, w, h, offset, 77);
            Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
        }
    }

    [Fact]
    public void FindVerticalOffset_LargeShift_ReturnsExactOffset()
    {
        const int w = 100, h = 100, offset = 85;
        var oldFrame = MakeNoise(w, h, 41);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 42);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    [Fact]
    public void FindVerticalOffset_IdenticalFrames_ReturnsZero()
    {
        const int w = 200, h = 120;
        var frame = MakeNoise(w, h, 51);
        Assert.Equal(0, ImageAligner.FindVerticalOffset(frame, frame, w, h));
    }

    // ---- 无重叠 / 无特征 ----

    [Fact]
    public void FindVerticalOffset_NoOverlap_ReturnsNull()
    {
        const int w = 200, h = 120;
        var a = MakeNoise(w, h, 61);
        var b = MakeNoise(w, h, 62);
        Assert.Null(ImageAligner.FindVerticalOffset(a, b, w, h));
    }

    [Fact]
    public void FindVerticalOffset_SolidColor_ReturnsNull()
    {
        const int w = 100, h = 80;
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = 128; px[i + 1] = 128; px[i + 2] = 128; px[i + 3] = 255;
        }
        Assert.Null(ImageAligner.FindVerticalOffset(px, px, w, h));
    }

    // ---- 真实页面:行内容高度相似(如 notepad 每行仅行号不同) ----

    /// <summary>模拟文本页面:每行 "Line N - padding..." 仅行号不同,其余像素几乎相同。</summary>
    private static byte[] MakeTextLines(int w, int h, int seed, int lineHeight = 3)
    {
        var rnd = new Random(seed);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int lineNo = y / lineHeight;
            string text = $"Line {lineNo} - padding padding padding padding 0123456789";
            for (int x = 0; x < w; x++)
            {
                byte v = 245;
                int charIdx = x / 7;
                if (charIdx < text.Length && text[charIdx] != ' ')
                    v = (byte)(60 + text[charIdx] * 7 % 80);
                int i = (y * w + x) * 4;
                px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
            }
        }
        return px;
    }

    [Fact]
    public void FindVerticalOffset_SimilarTextLines_ReturnsExactOffset()
    {
        // 纯 SAD 区分度不足(错误偏移的逐行 MAD 与正确偏移接近),行指纹匹配必须恢复偏移
        const int w = 240, h = 96, offset = 30;
        var oldFrame = MakeTextLines(w, h, 7);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 8);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    [Fact]
    public void FindVerticalOffset_SimilarTextLines_WideFrame_ReturnsExactOffset()
    {
        // 真实 notepad 参数:宽帧 + 文本左对齐 + 行高 21px;行号差异在左缘,
        // 若左缘被边缘忽略吞掉,所有行内容在采样列上相同 → 任何偏移 MAD 接近 → 无法对齐
        const int w = 1920, h = 105, offset = 63;
        var oldFrame = MakeTextLines(w, h, 13, 21);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 14);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    [Fact]
    public void FindVerticalOffset_SimilarTextLines_NoiseRobust()
    {
        const int w = 240, h = 96, offset = 30;
        var oldFrame = MakeTextLines(w, h, 9);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 10);
        AddNoise(oldFrame, 11, 4);
        AddNoise(newFrame, 12, 4);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    // ---- 鲁棒性 ----

    [Fact]
    public void FindVerticalOffset_NoiseRobust_StillRecoversOffset()
    {
        const int w = 240, h = 150, offset = 45;
        var oldFrame = MakeGradient(w, h, 71);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 72);
        AddNoise(oldFrame, 73, 8);
        AddNoise(newFrame, 74, 8);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    [Fact]
    public void FindVerticalOffset_ScrollbarStrip_Ignored()
    {
        // 右缘 8% 模拟滚动条:两帧图案完全无关,不影响对齐
        const int w = 240, h = 150, offset = 30;
        const int strip = (int)(w * 0.08);
        var oldFrame = MakeGradient(w, h, 81);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 82);
        var rnd = new Random(83);
        for (int y = 0; y < h; y++)
        for (int x = w - strip; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = (byte)rnd.Next(256);
            newFrame[i] = v; newFrame[i + 1] = v; newFrame[i + 2] = v;
        }
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h));
    }

    // ---- 参数与格式 ----

    [Fact]
    public void FindVerticalOffset_InvalidBuffer_Throws()
    {
        const int w = 100, h = 80;
        var a = MakeNoise(w, h, 91);
        var b = MakeNoise(w - 1, h, 92);
        Assert.Throws<ArgumentException>(() => ImageAligner.FindVerticalOffset(a, b, w, h));
    }

    [Fact]
    public void FindVerticalOffset_Rgb24_Works()
    {
        const int w = 200, h = 120, offset = 20;
        var oldFrame = MakeNoise(w, h, 101, 3);
        var newFrame = ScrollDown(oldFrame, w, h, offset, 102, 3);
        Assert.Equal(offset, ImageAligner.FindVerticalOffset(oldFrame, newFrame, w, h, 3));
    }
}
