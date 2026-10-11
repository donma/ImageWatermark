using ImageWatermarkPortable.Services;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SxImageRgba = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using SxPointF = SixLabors.ImageSharp.PointF;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Tests;

public sealed class GlyphClippingTests
{
    [Theory]
    [InlineData("W")]
    [InlineData("W W")]
    [InlineData("W a b")]
    [InlineData("繁體中文字")]
    [InlineData("W 測試")]
    [InlineData("版權所有 © MyBrand 2026")]
    public void Rotated_Glyph_Preserves_Full_Text_Ink(string text)
    {
        var renderer = new WatermarkRenderer();
        var font = renderer.CreateFontForTest(72);
        var options = new RichTextOptions(font)
        {
            Origin = new SxPointF(0, 0),
            HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment.Left,
            VerticalAlignment = SixLabors.Fonts.VerticalAlignment.Top,
        };
        var bounds = TextMeasurer.MeasureBounds(text, options);
        var opaque = new SxRgba32(255, 255, 255, 255);

        using var reference = new SxImageRgba((int)Math.Ceiling(bounds.Width) + 200, (int)Math.Ceiling(bounds.Height) + 200);
        reference.Mutate(c => c.DrawText(text, font, opaque, new SxPointF(100 - bounds.X, 100 - bounds.Y)));
        var refInk = CountInk(reference);

        using var glyph = renderer.BuildGlyphImage(text, font, bounds, Math.Max(1.0, bounds.Width), Math.Max(1.0, bounds.Height), opaque, 72);
        var glyphInk = CountInk(glyph);

        Assert.True(
            glyphInk >= refInk * 0.90,
            $"text='{text}': rotated glyph ink {glyphInk} < 90% of reference {refInk} (clipped)");
    }

    private static int CountInk(SxImageRgba image)
    {
        var count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A > 20) count++;
                }
            }
        });
        return count;
    }
}
