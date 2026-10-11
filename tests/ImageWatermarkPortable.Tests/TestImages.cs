using ImageWatermarkPortable.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SxImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using SxImageFactory = SixLabors.ImageSharp.Image;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Tests;

internal static class TestImages
{
    public static SxImage Solid(int width, int height, Rgba32 color)
        => new(width, height, color);

    public static SxImage Transparent(int width, int height)
        => new(width, height, new Rgba32(0, 0, 0, 0));

    public static void SaveJpg(SxImage image, string path, int quality = 90)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsJpeg(path, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = quality });
    }

    public static void SavePng(SxImage image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsPng(path);
    }

    public static void SaveWebp(SxImage image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsWebp(path);
    }

    public static SxImage Load(string path)
        => SxImageFactory.Load<SxRgba32>(path);

    public static string FormatName(string path)
    {
        using var stream = File.OpenRead(path);
        var format = SxImageFactory.DetectFormat(stream);
        return format?.Name ?? "unknown";
    }

    public static Rgba32[,] Region(SxImage image, int x, int y, int width, int height)
    {
        var result = new Rgba32[width, height];
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                result[col, row] = image[x + col, y + row];
            }
        }
        return result;
    }

    public static int CountBright(Rgba32[,] region, byte threshold)
    {
        var count = 0;
        foreach (var p in region)
        {
            if (p.R >= threshold) count++;
        }
        return count;
    }

    public static byte MaxRed(Rgba32[,] region)
    {
        byte max = 0;
        foreach (var p in region)
        {
            if (p.R > max) max = p.R;
        }
        return max;
    }
}

internal sealed class ProgressCollector : IProgress<BatchProgress>
{
    public BatchProgress? Last { get; private set; }

    public void Report(BatchProgress value) => Last = value;
}
