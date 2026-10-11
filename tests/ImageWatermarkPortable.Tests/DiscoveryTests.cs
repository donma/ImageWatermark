using System.Diagnostics;
using ImageWatermarkPortable.Services;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Tests;

public sealed class DiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iwp-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Combine(_root, Path.Combine(parts));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void Finds_SupportedFiles_Recursively_And_IgnoresOthers()
    {
        var src = Dir("src");
        Touch(Path.Combine(src, "a.jpg"));
        Touch(Path.Combine(src, "b.JPEG"));
        Touch(Path.Combine(src, "c.png"));
        Touch(Path.Combine(src, "d.webp"));
        Touch(Path.Combine(src, "note.txt"));
        Touch(Path.Combine(src, "sub1", "e.jpg"));
        Touch(Path.Combine(src, "sub1", "sub2", "f.png"));
        Touch(Path.Combine(src, "vector.svg"));

        var result = new ImageDiscoveryService().EnumerateImages(src);

        Assert.Equal(6, result.Count);
        Assert.All(result, p => Assert.True(ImageDiscoveryService.IsSupportedFile(p)));
    }

    [Fact]
    public void Returns_Empty_For_MissingDirectory()
    {
        var result = new ImageDiscoveryService().EnumerateImages(Path.Combine(_root, "nope"));
        Assert.Empty(result);
    }

    [Fact]
    public void Does_Not_Recursion_Into_Junction()
    {
        var src = Dir("j-src");
        var outside = Dir("j-outside");
        Touch(Path.Combine(src, "inside.jpg"));
        Touch(Path.Combine(outside, "outside.jpg"));

        var link = Path.Combine(src, "link");
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{outside}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(10000);

        if (!Directory.Exists(link))
        {
            return;
        }

        var result = new ImageDiscoveryService().EnumerateImages(src);

        Assert.Single(result);
        Assert.DoesNotContain(result, p => p.Contains("outside.jpg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindFirstValidImage_SkipsCorruptFile()
    {
        var src = Dir("first-src");
        File.WriteAllBytes(Path.Combine(src, "broken.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x11 });
        TestImages.SaveJpg(TestImages.Solid(20, 20, new SxRgba32(10, 200, 30, 255)), Path.Combine(src, "good.jpg"));

        var first = new ImageDiscoveryService().FindFirstValidImage(src);

        Assert.NotNull(first);
        Assert.EndsWith("good.jpg", first);
    }
}
