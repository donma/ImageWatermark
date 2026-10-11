using ImageWatermarkPortable.Models;
using SixLabors.ImageSharp.Processing;
using SxImage = SixLabors.ImageSharp.Image;
using SxImageRgba = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Services;

public sealed class BatchProcessingService
{
    private const int PreviewThumbSize = 800;
    private const long ThumbIntervalMs = 120;

    private readonly WatermarkRenderer _renderer;
    private readonly ImageEncoderService _encoder;
    private readonly SafeFileWriter _safeWriter;
    private readonly System.Diagnostics.Stopwatch _thumbClock = System.Diagnostics.Stopwatch.StartNew();

    public BatchProcessingService(WatermarkRenderer renderer, ImageEncoderService encoder, SafeFileWriter safeWriter)
    {
        _renderer = renderer;
        _encoder = encoder;
        _safeWriter = safeWriter;
    }

    public async Task<BatchResult> ProcessAsync(
        IReadOnlyList<string> files,
        string sourceRoot,
        string destinationRoot,
        WatermarkOptions options,
        IProgress<BatchProgress> progress,
        CancellationToken cancellationToken)
    {
        var total = files.Count;
        var succeeded = 0;
        var failed = 0;
        var processed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var logger = new ErrorLogService(destinationRoot);
        logger.StartSession(sourceRoot, destinationRoot, total);

        var sourceRootFull = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destRootFull = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceRootFull, file);
            var destPath = Path.GetFullPath(Path.Combine(destRootFull, relativePath));

            if (!PathSecurity.IsPathInsideRoot(destRootFull, destPath))
            {
                Interlocked.Increment(ref failed);
                Interlocked.Increment(ref processed);
                logger.LogError(relativePath, "PathValidation", "Destination path escapes output root.");
                Report(progress, total, processed, succeeded, failed, relativePath, null);
                continue;
            }

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            var tempPath = _safeWriter.GetTempPathFor(destPath);
            var stage = "Decode";
            Bitmap? preview = null;
            try
            {
                using var image = await LoadAndNormalizeAsync(file, cancellationToken).ConfigureAwait(false);
                stage = "Watermark";
                _renderer.ApplyWatermark(image, options.Text, options.OpacityPercent, options.FontSizePx, options.Color);
                stage = "Encode";
                await _encoder.EncodeAsync(image, file, tempPath, cancellationToken).ConfigureAwait(false);
                stage = "Replace";
                _safeWriter.ReplaceFile(tempPath, destPath);

                if (_thumbClock.ElapsedMilliseconds >= ThumbIntervalMs)
                {
                    try
                    {
                        preview = _renderer.ToPreviewBitmap(image, PreviewThumbSize);
                        _thumbClock.Restart();
                    }
                    catch
                    {
                        preview?.Dispose();
                        preview = null;
                    }
                }

                Interlocked.Increment(ref succeeded);
            }
            catch (OperationCanceledException)
            {
                _safeWriter.CleanupTemp(tempPath);
                preview?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                _safeWriter.CleanupTemp(tempPath);
                Interlocked.Increment(ref failed);
                logger.LogError(relativePath, stage, ex.Message);
            }
            finally
            {
                Interlocked.Increment(ref processed);
            }

            Report(progress, total, processed, succeeded, failed, relativePath, preview);
        }

        sw.Stop();
        logger.FinishSession(succeeded, failed, sw.Elapsed);

        return new BatchResult
        {
            Total = total,
            Succeeded = succeeded,
            Failed = failed,
            Elapsed = sw.Elapsed,
        };
    }

    private static void Report(IProgress<BatchProgress> progress, int total, int processed, int succeeded, int failed, string file, Bitmap? preview)
    {
        progress?.Report(new BatchProgress
        {
            Total = total,
            Processed = processed,
            Succeeded = succeeded,
            Failed = failed,
            CurrentFile = file,
            Preview = preview,
        });
    }

    private static async Task<SxImageRgba> LoadAndNormalizeAsync(string path, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var image = await SxImage.LoadAsync<SxRgba32>(fs, cancellationToken).ConfigureAwait(false);

        if (image.Frames.Count > 1)
        {
            image.Dispose();
            throw new NotSupportedException("動畫圖片（Animated WebP/PNG）不支援，已略過。");
        }

        image.Mutate(x => x.AutoOrient());

        var profile = image.Metadata.ExifProfile;
        if (profile is not null)
        {
            profile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, (ushort)1);
        }

        return image;
    }
}

public sealed class BatchResult
{
    public int Total { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public TimeSpan Elapsed { get; init; }
}
