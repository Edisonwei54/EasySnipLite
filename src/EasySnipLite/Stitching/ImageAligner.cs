namespace EasySnipLite.Stitching;

/// <summary>
/// M4 长截图帧对齐纯逻辑:灰度化 + 宽度降采样 + 按行 SAD 两级搜索垂直偏移。
/// 无 UI / 位图依赖,可单测。
/// </summary>
public static class ImageAligner
{
    /// <summary>忽略右缘比例(滚动条等易变区域不参与对齐)。</summary>
    private const double IgnoreRightFraction = 0.06;

    /// <summary>列降采样步长。</summary>
    private const int ColStride = 4;

    /// <summary>粗搜行采样步长。</summary>
    private const int RowStride = 8;

    /// <summary>细搜在粗偏移附近的精确行搜索半径。</summary>
    private const int FineRadius = RowStride;

    /// <summary>可靠匹配的最大平均绝对差(0-255 灰度)。</summary>
    private const double MaxMeanAbsDiff = 10.0;

    /// <summary>
    /// 求新帧相对旧帧的垂直偏移:即滚动后新帧顶部内容在旧帧中下移的行数。
    /// 两帧无可靠重叠(滚动过快跳页、纯色无特征)时返回 null。
    /// </summary>
    public static int? FindVerticalOffset(byte[] oldPixels, byte[] newPixels, int width, int height, int bytesPerPixel = 4)
    {
        int expected = width * height * bytesPerPixel;
        if (oldPixels.Length != expected || newPixels.Length != expected)
            throw new ArgumentException("像素缓冲区长度与 width*height*bytesPerPixel 不一致。");

        var oldGray = ToGrayColumnSamples(oldPixels, width, height, bytesPerPixel);
        var newGray = ToGrayColumnSamples(newPixels, width, height, bytesPerPixel);

        // 纯色 / 无特征区域无法对齐
        if (Variance(oldGray) < 1e-6) return null;

        // 粗搜:行采样步进 RowStride,候选偏移 0..height-1
        double bestMad = double.MaxValue;
        int bestD = 0;
        for (int d = 0; d < height; d++)
        {
            double mad = MeanAbsDiff(oldGray, newGray, d, RowStride);
            if (mad < bestMad)
            {
                bestMad = mad;
                bestD = d;
            }
        }

        // 细搜:粗偏移附近 ±FineRadius 用全部行精确计算
        int lo = Math.Max(0, bestD - FineRadius);
        int hi = Math.Min(height - 1, bestD + FineRadius);
        for (int d = lo; d <= hi; d++)
        {
            double mad = MeanAbsDiff(oldGray, newGray, d, 1);
            if (mad < bestMad)
            {
                bestMad = mad;
                bestD = d;
            }
        }

        return bestMad <= MaxMeanAbsDiff ? bestD : (int?)null;
    }

    /// <summary>
    /// 灰度化并抽取采样列,返回 [height, cols] 灰度矩阵。
    /// 只忽略右缘(滚动条):左缘文本行号/缩进是重要对齐特征,忽略会导致行内容相似的页面无法对齐。
    /// </summary>
    private static byte[,] ToGrayColumnSamples(byte[] px, int width, int height, int bpp)
    {
        int startX = 0;
        int endX = width - (int)(width * IgnoreRightFraction);
        int cols = Math.Max(0, (endX - startX + ColStride - 1) / ColStride);
        var g = new byte[height, cols];
        for (int y = 0; y < height; y++)
        {
            int col = 0;
            for (int x = startX; x < endX; x += ColStride)
            {
                int i = (y * width + x) * bpp;
                int b = px[i], gc = px[i + 1], r = px[i + 2];
                g[y, col++] = (byte)((77 * r + 150 * gc + 29 * b) >> 8);
            }
        }
        return g;
    }

    /// <summary>偏移 d 下两帧的平均绝对差:new 第 y 行 vs old 第 y+d 行,y 按 rowStride 采样。</summary>
    private static double MeanAbsDiff(byte[,] oldGray, byte[,] newGray, int d, int rowStride)
    {
        int height = oldGray.GetLength(0);
        int cols = oldGray.GetLength(1);
        int rows = (height - d + rowStride - 1) / rowStride;
        if (rows <= 0) return double.MaxValue;

        long sum = 0;
        for (int y = 0; y < height - d; y += rowStride)
        {
            for (int c = 0; c < cols; c++)
            {
                int diff = newGray[y, c] - oldGray[y + d, c];
                sum += diff < 0 ? -diff : diff;
            }
        }
        return (double)sum / (rows * cols);
    }

    /// <summary>灰度矩阵(行采样)的亮度方差;约 0 表示纯色无特征。</summary>
    private static double Variance(byte[,] gray)
    {
        int height = gray.GetLength(0);
        int cols = gray.GetLength(1);
        long sum = 0, sumSq = 0, n = 0;
        for (int y = 0; y < height; y += RowStride)
        {
            for (int c = 0; c < cols; c++)
            {
                byte v = gray[y, c];
                sum += v;
                sumSq += v * v;
                n++;
            }
        }
        if (n == 0) return 0;
        double mean = (double)sum / n;
        return (double)sumSq / n - mean * mean;
    }
}
