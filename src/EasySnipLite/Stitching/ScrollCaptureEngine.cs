using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EasySnipLite.Stitching;

/// <summary>滚动捕获参数。</summary>
public sealed record ScrollCaptureOptions
{
    /// <summary>每次滚动的格数(120/格)。</summary>
    public int WheelNotches  { get; set; } = 3;

    /// <summary>滚动后等待内容稳定的毫秒数。</summary>
    public int StabilizeDelayMs  { get; set; } = 350;

    /// <summary>连续内容未变的帧数达到该值判定到底。</summary>
    public int BottomConfirmCount  { get; set; } = 2;

    /// <summary>单次对齐失败的重试次数(重新截帧)。</summary>
    public int RetryAttempts  { get; set; } = 2;

    /// <summary>长图高度上限(像素)。</summary>
    public int MaxHeight  { get; set; } = 20000;

    /// <summary>低于该偏移(行)视为内容未变(滚动无效/到底)。</summary>
    public int MinScrollOffset  { get; set; } = 3;
}

/// <summary>滚动捕获结果。</summary>
public sealed record ScrollCaptureResult
{
    /// <summary>拼接结果(调用方负责 Dispose);对齐失败时为空(画布在 Checkpoint 中)。</summary>
    public Bitmap? Image { get; init; }

    public int Height { get; init; }
    public int FrameCount { get; init; }

    /// <summary>对齐失败的接缝位置(长图 y 坐标,预览标红用)。</summary>
    public IReadOnlyList<int> FailedSeams { get; init; } = [];

    public bool ReachedBottom { get; init; }
    public bool Cancelled { get; init; }
    public bool HeightLimitReached { get; init; }

    /// <summary>对齐失败/截帧失败等错误信息。</summary>
    public string? Error { get; init; }

    /// <summary>对齐失败时的运行状态(供重试续跑);Image 为空时必不为空。</summary>
    public ScrollCaptureCheckpoint? Checkpoint { get; init; }
}

/// <summary>捕获中途的运行状态,供对齐失败后重试续跑(不丢弃已拼接内容)。</summary>
public sealed record ScrollCaptureCheckpoint
{
    public required Bitmap Canvas { get; init; }
    public required byte[] PrevPixels { get; init; }
    public required int CanvasH { get; init; }
    public required int FrameCount { get; init; }
    public required int BottomCount { get; init; }
}

/// <summary>
/// M4 长截图调度循环:截帧 → 对齐上一帧 → 拼接 → 滚动 → 等待稳定 → 重复。
/// 截帧与滚动均通过委托注入,便于单测(合成帧序列)与真实捕获(屏幕 BitBlt)复用。
/// </summary>
public sealed class ScrollCaptureEngine
{
    private readonly Func<Bitmap> _captureFrame;
    private readonly Action _scroll;
    private readonly ScrollCaptureOptions _options;

    /// <summary>每帧拼接后的长图快照(调用方只读,不得持有引用)。</summary>
    public event Action<Bitmap>? PreviewUpdated;

    /// <summary>进度:(当前高度, 已用帧数)。</summary>
    public event Action<int, int>? ProgressChanged;

    public ScrollCaptureEngine(Func<Bitmap> captureFrame, Action scroll, ScrollCaptureOptions? options = null)
    {
        _captureFrame = captureFrame;
        _scroll = scroll;
        _options = options ?? new ScrollCaptureOptions();
    }

    public Task<ScrollCaptureResult> RunAsync(CancellationToken ct) => RunCoreAsync(null, ct);

    /// <summary>从失败时的检查点续跑(保留已拼接内容,不重新滚动)。</summary>
    public Task<ScrollCaptureResult> ContinueAsync(ScrollCaptureCheckpoint checkpoint, CancellationToken ct) =>
        RunCoreAsync(checkpoint, ct);

    private async Task<ScrollCaptureResult> RunCoreAsync(ScrollCaptureCheckpoint? start, CancellationToken ct)
    {
        Bitmap? canvas = start?.Canvas;
        byte[]? prevPixels = start?.PrevPixels;
        int canvasH = start?.CanvasH ?? 0;
        int frameCount = start?.FrameCount ?? 0;
        int bottomCount = start?.BottomCount ?? 0;
        int retries = 0;
        var seams = new List<int>();
        string? error = null;
        bool reachedBottom = false, heightLimit = false, cancelled = false;
        ScrollCaptureCheckpoint? checkpoint = null;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            // 1. 截帧
            Bitmap frame;
            try
            {
                frame = _captureFrame();
            }
            catch (Exception ex)
            {
                error = $"截帧失败:{ex.Message}";
                break;
            }
            if (frame is null)
            {
                error = "截帧失败:返回空帧";
                break;
            }

            using (frame)
            {
                var pixels = ToBgra(frame);

                if (prevPixels is null)
                {
                    // 首帧:作为长图起点
                    canvas?.Dispose();
                    canvas = new Bitmap(frame);
                    canvasH = frame.Height;
                    prevPixels = pixels;
                    frameCount = 1;
                    Emit(canvas, canvasH, frameCount);
                }
                else
                {
                    // 2. 与上一帧对齐
                    int? offset = ImageAligner.FindVerticalOffset(prevPixels, pixels, frame.Width, frame.Height);
                    if (offset is null)
                    {
                        // 对齐失败:可能页面仍在滚动/加载,等待后重截
                        retries++;
                        if (retries > _options.RetryAttempts)
                        {
                            seams.Add(canvasH);
                            error = "对齐失败:滚动过快或页面持续变化,请重试";
                            break;
                        }
                        await Task.Delay(_options.StabilizeDelayMs, ct);
                        continue;
                    }
                    retries = 0;

                    if (offset.Value < _options.MinScrollOffset)
                    {
                        // 内容未变:滚动无效,可能到底
                        bottomCount++;
                        if (bottomCount >= _options.BottomConfirmCount)
                        {
                            reachedBottom = true;
                            break;
                        }
                    }
                    else
                    {
                        // 3. 拼接新内容(裁掉与上一帧重叠的行)
                        bottomCount = 0;
                        int newRows = frame.Height - offset.Value;
                        canvas = Grow(canvas!, frame, offset.Value, newRows);
                        canvasH += newRows;
                        prevPixels = pixels;
                        frameCount++;
                        Emit(canvas, canvasH, frameCount);
                    }
                }

                if (canvasH >= _options.MaxHeight)
                {
                    heightLimit = true;
                    break;
                }
                if (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
            }

            // 4. 滚动并等待内容稳定
            _scroll();
            try
            {
                await Task.Delay(_options.StabilizeDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
        }

        // 对齐失败:画布移交给 Checkpoint 供重试续跑,调用方不得同时持有 Image
        if (error is not null && canvas is not null)
        {
            checkpoint = new ScrollCaptureCheckpoint
            {
                Canvas = canvas,
                PrevPixels = prevPixels!,
                CanvasH = canvasH,
                FrameCount = frameCount,
                BottomCount = bottomCount,
            };
        }

        return new ScrollCaptureResult
        {
            Image = error is null ? canvas : null,
            Height = canvasH,
            FrameCount = frameCount,
            FailedSeams = seams,
            ReachedBottom = reachedBottom,
            Cancelled = cancelled,
            HeightLimitReached = heightLimit,
            Error = error,
            Checkpoint = checkpoint,
        };
    }

    private void Emit(Bitmap canvas, int height, int frames)
    {
        ProgressChanged?.Invoke(height, frames);
        PreviewUpdated?.Invoke(canvas);
    }

    /// <summary>把 canvas 尾部追加新帧的 newRows 行(去掉与上一帧重叠部分)。</summary>
    private static Bitmap Grow(Bitmap canvas, Bitmap frame, int offset, int newRows)
    {
        var dst = new Bitmap(frame.Width, canvas.Height + newRows, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.DrawImage(canvas, 0, 0, canvas.Width, canvas.Height);
            g.DrawImage(frame, new System.Drawing.Rectangle(0, canvas.Height, frame.Width, newRows),
                new System.Drawing.Rectangle(0, offset, frame.Width, newRows), GraphicsUnit.Pixel);
        }
        canvas.Dispose();
        return dst;
    }

    /// <summary>位图 → BGRA 像素(Format32bppArgb 的 stride 恒为 width*4)。</summary>
    private static byte[] ToBgra(Bitmap bmp)
    {
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
