using System.Security.Cryptography;
using ImageWatermarkPortable.Models;
using ImageWatermarkPortable.Services;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Tests;

public sealed class BatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iwp-tests", Guid.NewGuid().ToString("N"));
    private readonly WatermarkRenderer _renderer = new();

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private (string Source, string Dest) MakeFolders()
    {
        var source = Path.Combine(_root, "in");
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        return (source, dest);
    }

    private static WatermarkOptions Options(string text = "測試浮水印 Test 2026") => new()
    {
        Text = text,
        OpacityPercent = 30,
        FontSizePx = 36,
    };

    private async Task<BatchResult> RunAsync(string source, string dest, WatermarkOptions? options = null, ProgressCollector? progress = null)
    {
        var files = new ImageDiscoveryService().EnumerateImages(source);
        var batch = new BatchProcessingService(_renderer, new ImageEncoderService(), new SafeFileWriter());
        return await batch.ProcessAsync(files, source, dest, options ?? Options(), progress ?? new ProgressCollector(), CancellationToken.None);
    }

    [Fact]
    public async Task MixedFormats_PreserveStructureFormatAndSize()
    {
        var (source, dest) = MakeFolders();
        using (var jpg = TestImages.Solid(640, 480, new SxRgba32(180, 60, 40, 255)))
            TestImages.SaveJpg(jpg, Path.Combine(source, "sub1", "a.jpg"));
        using (var png = TestImages.Transparent(300, 200))
        {
            png[10, 10] = new SxRgba32(20, 200, 40, 255);
            png[20, 20] = new SxRgba32(0, 0, 0, 0);
            TestImages.SavePng(png, Path.Combine(source, "sub1", "sub2", "b.png"));
        }
        using (var webp = TestImages.Solid(200, 150, new SxRgba32(30, 60, 200, 255)))
            TestImages.SaveWebp(webp, Path.Combine(source, "c.webp"));
        File.WriteAllBytes(Path.Combine(source, "broken.jpg"), new byte[] { 1, 2, 3, 4, 5 });
        File.WriteAllText(Path.Combine(source, "notes.txt"), "ignore me");

        var progress = new ProgressCollector();
        var result = await RunAsync(source, dest, progress: progress);

        Assert.Equal(4, result.Total);
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(1, result.Failed);

        var outJpg = Path.Combine(dest, "sub1", "a.jpg");
        var outPng = Path.Combine(dest, "sub1", "sub2", "b.png");
        var outWebp = Path.Combine(dest, "c.webp");
        Assert.True(File.Exists(outJpg));
        Assert.True(File.Exists(outPng));
        Assert.True(File.Exists(outWebp));

        Assert.Equal("JPEG", TestImages.FormatName(outJpg));
        Assert.Equal("PNG", TestImages.FormatName(outPng));
        Assert.Equal("Webp", TestImages.FormatName(outWebp));

        using (var outImg = TestImages.Load(outJpg))
        {
            Assert.Equal(640, outImg.Width);
            Assert.Equal(480, outImg.Height);
        }
        using (var outImg = TestImages.Load(outPng))
        {
            Assert.Equal(300, outImg.Width);
            Assert.Equal(200, outImg.Height);
            Assert.Equal(255, outImg[10, 10].A);
        }

        Assert.NotNull(progress.Last);
        Assert.Equal(4, progress.Last!.Processed);
        Assert.Equal(3, progress.Last.Succeeded);
        Assert.Equal(1, progress.Last.Failed);
        Assert.Equal(100, progress.Last.Percent);

        var log = Path.Combine(dest, "watermark-errors.log");
        Assert.True(File.Exists(log));
        var logText = File.ReadAllText(log);
        Assert.Contains("broken.jpg", logText);
        Assert.Contains("Failed: 1", logText);
    }

    [Fact]
    public async Task SourceFiles_AreNotModified_And_Overwrite_Works()
    {
        var (source, dest) = MakeFolders();
        var jpgPath = Path.Combine(source, "a.jpg");
        using (var jpg = TestImages.Solid(320, 240, new SxRgba32(90, 90, 90, 255)))
            TestImages.SaveJpg(jpg, jpgPath);

        var hashBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jpgPath)));

        var r1 = await RunAsync(source, dest, Options("第一版浮水印"));
        Assert.Equal(1, r1.Succeeded);

        var firstOutput = File.ReadAllBytes(Path.Combine(dest, "a.jpg"));

        var r2 = await RunAsync(source, dest, Options("第二版浮水印"));
        Assert.Equal(1, r2.Succeeded);

        var secondOutput = File.ReadAllBytes(Path.Combine(dest, "a.jpg"));
        Assert.False(firstOutput.AsSpan().SequenceEqual(secondOutput), "第二版輸出應覆寫並不同於第一版");

        var hashAfter = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jpgPath)));
        Assert.Equal(hashBefore, hashAfter);
    }

    [Fact]
    public async Task Exif_Orientation_Is_Normalized()
    {
        var (source, dest) = MakeFolders();
        var path = Path.Combine(source, "rotated.jpg");
        using (var img = TestImages.Solid(200, 100, new SxRgba32(120, 120, 120, 255)))
        {
            var profile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
            profile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, (ushort)6);
            img.Metadata.ExifProfile = profile;
            TestImages.SaveJpg(img, path);
        }

        var result = await RunAsync(source, dest);
        Assert.Equal(1, result.Succeeded);

        using var output = TestImages.Load(Path.Combine(dest, "rotated.jpg"));
        Assert.Equal(100, output.Width);
        Assert.Equal(200, output.Height);

        var outProfile = output.Metadata.ExifProfile;
        if (outProfile is not null &&
            outProfile.TryGetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, out var value))
        {
            Assert.Equal((ushort)1, value.Value);
        }
    }

    [Fact]
    public async Task LockedDestination_File_Fails_Safely_Without_TempLeftovers()
    {
        var (source, dest) = MakeFolders();
        using (var jpg = TestImages.Solid(200, 150, new SxRgba32(70, 70, 70, 255)))
            TestImages.SaveJpg(jpg, Path.Combine(source, "a.jpg"));

        var destFile = Path.Combine(dest, "a.jpg");
        File.WriteAllText(destFile, "existing-content");
        var existingBytes = File.ReadAllBytes(destFile);

        using (var _lock = new FileStream(destFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await RunAsync(source, dest);
            Assert.Equal(0, result.Succeeded);
            Assert.Equal(1, result.Failed);
        }

        Assert.Equal(existingBytes, File.ReadAllBytes(destFile));
        Assert.Empty(Directory.GetFiles(dest, "*.tmp"));
        var logText = File.ReadAllText(Path.Combine(dest, "watermark-errors.log"));
        Assert.Contains("a.jpg", logText);
    }

    [Fact]
    public async Task EmptySource_ReturnsZeroCounts_WithoutError()
    {
        var (source, dest) = MakeFolders();
        var result = await RunAsync(source, dest);

        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(Path.Combine(dest, "watermark-errors.log")));
    }

    [Fact]
    public async Task Unicode_FileNames_Preserved()
    {
        var (source, dest) = MakeFolders();
        using (var jpg = TestImages.Solid(120, 90, new SxRgba32(50, 60, 70, 255)))
            TestImages.SaveJpg(jpg, Path.Combine(source, "中文檔名 測試.jpg"));

        var result = await RunAsync(source, dest);

        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(dest, "中文檔名 測試.jpg")));
    }

    [Fact]
    public void BatchProgress_ZeroTotal_PercentIsZero()
    {
        var p = new BatchProgress { Total = 0, Processed = 0 };
        Assert.Equal(0, p.Percent);
    }
}
