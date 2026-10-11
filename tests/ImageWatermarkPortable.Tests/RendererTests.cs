using ImageWatermarkPortable.Services;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Tests;

public sealed class RendererTests
{
    private readonly WatermarkRenderer _renderer = new();

    private static SxRgba32[,] Region(SixLabors.ImageSharp.Image<SxRgba32> img, int x, int y, int w, int h)
        => TestImages.Region(img, x, y, w, h);

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    public void Four_Corners_Are_Covered(int width, int height)
    {
        using var image = TestImages.Solid(width, height, new SxRgba32(0, 0, 0, 255));
        _renderer.ApplyWatermark(image, "© MyBrand 2026", 40, 36);

        const int zone = 120;
        Assert.True(TestImages.CountBright(Region(image, 0, 0, zone, zone), 20) > 0, "top-left not covered");
        Assert.True(TestImages.CountBright(Region(image, width - zone, 0, zone, zone), 20) > 0, "top-right not covered");
        Assert.True(TestImages.CountBright(Region(image, 0, height - zone, zone, zone), 20) > 0, "bottom-left not covered");
        Assert.True(TestImages.CountBright(Region(image, width - zone, height - zone, zone, zone), 20) > 0, "bottom-right not covered");
    }

    [Fact]
    public void Center_Area_Is_Covered()
    {
        using var image = TestImages.Solid(1600, 900, new SxRgba32(0, 0, 0, 255));
        _renderer.ApplyWatermark(image, "WATERMARK", 40, 36);

        Assert.True(TestImages.CountBright(Region(image, 760, 410, 80, 80), 20) > 0);
    }

    [Fact]
    public void Opacity_25_Percent_White_On_Black()
    {
        using var image = TestImages.Solid(1200, 800, new SxRgba32(0, 0, 0, 255));
        _renderer.ApplyWatermark(image, "OPACITY", 25, 48);

        var max = TestImages.MaxRed(TestImages.Region(image, 0, 0, 1200, 800));
        Assert.InRange(max, 45, 80);
    }

    [Fact]
    public void Watermark_Uses_Requested_Color()
    {
        using var image = TestImages.Solid(1000, 700, new SxRgba32(0, 0, 0, 255));
        _renderer.ApplyWatermark(image, "REDWM", 25, 48, System.Drawing.Color.Red);

        var region = TestImages.Region(image, 0, 0, 1000, 700);
        var maxR = TestImages.MaxRed(region);
        Assert.InRange(maxR, 45, 80);

        byte maxG = 0;
        byte maxB = 0;
        foreach (var p in region)
        {
            if (p.G > maxG) maxG = p.G;
            if (p.B > maxB) maxB = p.B;
        }
        Assert.True(maxG < 15, $"green leaked into red watermark: {maxG}");
        Assert.True(maxB < 15, $"blue leaked into red watermark: {maxB}");
    }

    [Fact]
    public void TraditionalChinese_Text_Renders_Without_Throwing()
    {
        using var image = TestImages.Solid(800, 600, new SxRgba32(0, 0, 0, 255));
        _renderer.ApplyWatermark(image, "版權所有 © 2026 測試", 30, 36);

        Assert.True(TestImages.CountBright(TestImages.Region(image, 0, 0, 800, 600), 20) > 0);
    }

    [Fact]
    public void VeryLongText_Completes_And_Covers_Region()
    {
        var text = string.Concat(Enumerable.Repeat("很長的浮水印文字A1", 20));
        Assert.True(text.Length <= 200);

        using var image = TestImages.Solid(1000, 700, new SxRgba32(0, 0, 0, 255));
        _renderer.ApplyWatermark(image, text, 30, 36);

        Assert.True(TestImages.CountBright(TestImages.Region(image, 400, 300, 200, 100), 20) > 0);
    }

    [Fact]
    public void EmptyText_Does_Not_Modify_Pixels()
    {
        using var image = TestImages.Solid(400, 300, new SxRgba32(10, 20, 30, 255));
        using var before = image.Clone();

        _renderer.ApplyWatermark(image, "   ", 40, 36);

        for (var y = 0; y < image.Height; y += 37)
        {
            for (var x = 0; x < image.Width; x += 41)
            {
                Assert.Equal(before[x, y], image[x, y]);
            }
        }
    }

    [Fact]
    public void Transparent_Png_Composition_Keeps_Opaque_Area_Alpha()
    {
        using var image = TestImages.Transparent(600, 400);
        for (var y = 100; y < 300; y++)
        {
            for (var x = 150; x < 450; x++)
            {
                image[x, y] = new SxRgba32(200, 50, 50, 255);
            }
        }

        _renderer.ApplyWatermark(image, "WM", 30, 36);

        Assert.Equal(255, image[300, 200].A);

        byte maxAlpha = 0;
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                if (image[x, y].A > maxAlpha) maxAlpha = image[x, y].A;
            }
        }
        Assert.True(maxAlpha > 10, "watermark should be visible over transparent region");
    }

    [Fact]
    public void Preview_Render_Produces_Bitmap_With_Watermark()
    {
        using var source = TestImages.Solid(3000, 2000, new SxRgba32(0, 0, 0, 255));
        using var bmp = _renderer.RenderPreview(source, "PREVIEW", 30, 36, 1200);

        Assert.True(bmp.Width <= 1200);
        Assert.Equal(800, bmp.Height);

        var found = false;
        for (var y = 0; y < bmp.Height && !found; y += 3)
        {
            for (var x = 0; x < bmp.Width; x += 3)
            {
                if (bmp.GetPixel(x, y).R > 20)
                {
                    found = true;
                    break;
                }
            }
        }
        Assert.True(found, "preview bitmap should contain watermark pixels");
    }

    [Fact]
    public void Preview_Bitmap_Preserves_Rgb_Channel_Order()
    {
        using var source = TestImages.Solid(400, 300, new SxRgba32(255, 0, 0, 255));
        using var bmp = _renderer.RenderPreview(source, "WM", 25, 36, 400);

        var center = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
        Assert.True(center.R > 200, $"red channel lost: R={center.R}");
        Assert.True(center.B < 80, $"blue channel swapped in: B={center.B}");

        using var blueSource = TestImages.Solid(400, 300, new SxRgba32(0, 0, 255, 255));
        using var blueBmp = _renderer.RenderPreview(blueSource, "WM", 25, 36, 400);

        var blueCenter = blueBmp.GetPixel(blueBmp.Width / 2, blueBmp.Height / 2);
        Assert.True(blueCenter.B > 200, $"blue channel lost: B={blueCenter.B}");
        Assert.True(blueCenter.R < 80, $"red channel swapped in: R={blueCenter.R}");
    }
}
