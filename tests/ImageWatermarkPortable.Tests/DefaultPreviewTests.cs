using ImageWatermarkPortable.Forms;
using SxImageFactory = SixLabors.ImageSharp.Image;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Tests;

public sealed class DefaultPreviewTests
{
    [Fact]
    public void Embedded_Sample1_Exists_And_Decodes()
    {
        var asm = typeof(MainForm).Assembly;
        using var stream = asm.GetManifestResourceStream("ImageWatermarkPortable.sample1.jpg");

        Assert.NotNull(stream);
        using var image = SxImageFactory.Load<SxRgba32>(stream!);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }
}
