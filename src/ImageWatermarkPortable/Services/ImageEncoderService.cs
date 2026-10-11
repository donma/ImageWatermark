using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SxImageRgba = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace ImageWatermarkPortable.Services;

public sealed class ImageEncoderService
{
    private const int JpegQuality = 95;

    public async Task EncodeAsync(SxImageRgba image, string sourcePath, string tempPath, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(sourcePath);
        await using var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        IImageEncoder encoder = ext switch
        {
            ".jpg" or ".jpeg" => new JpegEncoder { Quality = JpegQuality },
            ".png" => new PngEncoder(),
            ".webp" => new WebpEncoder { Quality = 95 },
            _ => new PngEncoder(),
        };

        await image.SaveAsync(fs, encoder, cancellationToken).ConfigureAwait(false);
    }
}
