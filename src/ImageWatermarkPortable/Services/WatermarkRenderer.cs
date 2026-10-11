using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SxFont = SixLabors.Fonts.Font;
using SxFontFamily = SixLabors.Fonts.FontFamily;
using SxImage = SixLabors.ImageSharp.Image;
using SxImageRgba = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Services;

public sealed class WatermarkRenderer
{
    private const double RotationDegrees = 30.0;
    private const double MinScale = 0.5;
    private const double MaxScale = 3.0;
    private const double ReferenceSize = 1200.0;
    private const int MaxTileDraws = 4000;

    private static readonly string[] PreferredFontFamilies =
    {
        "Microsoft JhengHei",
        "Microsoft JhengHei UI",
        "Segoe UI",
        "Arial",
        "Tahoma",
        "DejaVu Sans",
        "Liberation Sans",
    };

    private SxFontFamily? _fontFamily;
    private readonly object _fontLock = new();

    public void ApplyWatermark(SxImageRgba image, string text, int opacityPercent, int fontSizePx, System.Drawing.Color? textColor = null)
    {
        if (image is null) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        var scale = ComputeScale(image.Width, image.Height);
        var effectiveFontSize = Math.Max(1, (int)Math.Round(fontSizePx * scale));
        var alpha = (byte)Math.Clamp((int)Math.Round(opacityPercent * 255.0 / 100.0), 0, 255);
        var baseColor = textColor ?? System.Drawing.Color.White;
        var color = new SxRgba32(baseColor.R, baseColor.G, baseColor.B, alpha);

        var font = CreateFont(effectiveFontSize);
        var textOptions = new RichTextOptions(font)
        {
            Origin = new SixLabors.ImageSharp.PointF(0, 0),
            HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment.Left,
            VerticalAlignment = SixLabors.Fonts.VerticalAlignment.Top,
        };

        var bounds = TextMeasurer.MeasureBounds(text, textOptions);
        var textWidth = Math.Max(1.0, bounds.Width);
        var textHeight = Math.Max(1.0, bounds.Height);

        var radians = RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var stepX = (int)Math.Ceiling(textWidth * 1.25 + effectiveFontSize * 0.5);
        var stepY = (int)Math.Ceiling(textHeight * 1.6 + effectiveFontSize * 0.5);
        if (stepX < 1) stepX = 1;
        if (stepY < 1) stepY = 1;

        using var glyph = BuildGlyphImage(text, font, bounds, textWidth, textHeight, color, effectiveFontSize);

        var width = image.Width;
        var height = image.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var diagonal = Math.Sqrt((double)width * width + (double)height * height);

        var rangeX = (int)Math.Ceiling(diagonal / 2 / stepX) + 2;
        var rangeY = (int)Math.Ceiling(diagonal / 2 / stepY) + 2;

        long totalDraws = (long)(rangeX * 2 + 1) * (rangeY * 2 + 1);
        if (totalDraws > MaxTileDraws)
        {
            var factor = Math.Sqrt((double)totalDraws / MaxTileDraws);
            rangeX = Math.Max(1, (int)Math.Ceiling(rangeX / factor));
            rangeY = Math.Max(1, (int)Math.Ceiling(rangeY / factor));
            if (stepX * (double)rangeX < diagonal / 2)
            {
                stepX = (int)Math.Ceiling(diagonal / 2 / rangeX) + 1;
            }
            if (stepY * (double)rangeY < diagonal / 2)
            {
                stepY = (int)Math.Ceiling(diagonal / 2 / rangeY) + 1;
            }
        }

        var offsetX = glyph.Width / 2f;
        var offsetY = glyph.Height / 2f;

        image.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                BlendPercentage = 1f,
                ColorBlendingMode = PixelColorBlendingMode.Normal,
            });

            for (var j = -rangeY; j <= rangeY; j++)
            {
                for (var i = -rangeX; i <= rangeX; i++)
                {
                    var px = centerX + (i * stepX) * cos - (j * stepY) * sin;
                    var py = centerY + (i * stepX) * sin + (j * stepY) * cos;
                    var location = new SixLabors.ImageSharp.Point((int)Math.Round(px - offsetX), (int)Math.Round(py - offsetY));
                    ctx.DrawImage(glyph, location, 1f);
                }
            }
        });
    }

    internal SxImageRgba BuildGlyphImage(
        string text,
        SxFont font,
        FontRectangle bounds,
        double textWidth,
        double textHeight,
        SxRgba32 color,
        int fontSizePx)
    {
        var pad = Math.Max(6, (int)Math.Ceiling(fontSizePx * 0.25));
        var canvasWidth = (int)Math.Ceiling(textWidth) + pad * 2;
        var canvasHeight = (int)Math.Ceiling(textHeight) + pad * 2;

        var canvas = new SxImageRgba(canvasWidth, canvasHeight);
        var drawX = (float)((canvasWidth - textWidth) / 2.0 - bounds.X);
        var drawY = (float)((canvasHeight - textHeight) / 2.0 - bounds.Y);
        canvas.Mutate(t => t.DrawText(text, font, color, new SixLabors.ImageSharp.PointF(drawX, drawY)));

        var rotated = canvas.Clone(t => t.Rotate((float)RotationDegrees));
        canvas.Dispose();
        return rotated;
    }

    public Bitmap RenderPreview(SxImageRgba source, string text, int opacityPercent, int fontSizePx, int previewMaxSize, System.Drawing.Color? textColor = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        using var preview = ResizeCopy(source, previewMaxSize);
        ApplyWatermark(preview, text, opacityPercent, fontSizePx, textColor);
        return ToBitmap(preview);
    }

    public Bitmap ToPreviewBitmap(SxImageRgba source, int previewMaxSize)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        using var resized = ResizeCopy(source, previewMaxSize);
        return ToBitmap(resized);
    }

    private static SxImageRgba ResizeCopy(SxImageRgba source, int previewMaxSize)
    {
        var scale = Math.Min(1.0, previewMaxSize / (double)Math.Max(source.Width, source.Height));
        var previewWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var previewHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Clone(c => c.Resize(previewWidth, previewHeight));
    }

    private static double ComputeScale(int width, int height)
    {
        var minSide = Math.Min(width, height);
        var scale = minSide / ReferenceSize;
        return Math.Clamp(scale, MinScale, MaxScale);
    }

    internal SxFont CreateFontForTest(int sizePx) => CreateFont(sizePx);

    private SxFont CreateFont(int sizePx)
    {
        var family = GetFontFamily();
        return family.CreateFont(sizePx, SixLabors.Fonts.FontStyle.Regular);
    }

    private SxFontFamily GetFontFamily()
    {
        if (_fontFamily is not null) return _fontFamily.Value;
        lock (_fontLock)
        {
            if (_fontFamily is not null) return _fontFamily.Value;
            var installed = SixLabors.Fonts.SystemFonts.Collection;
            foreach (var name in PreferredFontFamilies)
            {
                if (installed.TryGet(name, out var family))
                {
                    _fontFamily = family;
                    return family;
                }
            }

            foreach (var family in installed.Families)
            {
                _fontFamily = family;
                return family;
            }

            throw new InvalidOperationException("系統中找不到可用字型。");
        }
    }

    private static Bitmap ToBitmap(SxImageRgba image)
    {
        var bmp = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, image.Width, image.Height);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            var bytes = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(bytes);

            for (var i = 0; i < bytes.Length; i += 4)
            {
                (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
