using System.Buffers.Binary;
using IoPath = System.IO.Path;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace IconGenerator;

internal static class Program
{
    private const int MasterSize = 256;
    private static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        var outputDir = args.Length > 0 ? args[0] : IoPath.Combine(AppContext.BaseDirectory, "assets");
        Directory.CreateDirectory(outputDir);

        using var master = RenderMaster();
        master.SaveAsPng(IoPath.Combine(outputDir, "icon-256.png"));

        var bmpEntries = new List<(int Size, byte[] Data)>();
        var pngEntries = new List<(int Size, byte[] Data)>();

        foreach (var size in IconSizes)
        {
            byte[] png;
            using (var resized = size == MasterSize ? master.Clone() : master.Clone(c => c.Resize(size, size, KnownResamplers.Lanczos3)))
            {
                using var ms = new MemoryStream();
                resized.SaveAsPng(ms);
                png = ms.ToArray();

                if (size <= 64)
                {
                    bmpEntries.Add((size, EncodeBmpEntry(resized)));
                }
                else
                {
                    pngEntries.Add((size, png));
                }
            }

            File.WriteAllBytes(IoPath.Combine(outputDir, $"icon-{size}.png"), png);
        }

        var entries = bmpEntries.Concat(pngEntries).OrderBy(e => e.Size).ToList();
        var ico = BuildIco(entries);
        var icoPath = IoPath.Combine(outputDir, "icon.ico");
        File.WriteAllBytes(icoPath, ico);

        Console.WriteLine($"Icon written: {icoPath} ({ico.Length} bytes, {entries.Count} sizes)");
        return 0;
    }

    private static Image<Rgba32> RenderMaster()
    {
        var size = MasterSize;
        var image = new Image<Rgba32>(size, size, Color.Transparent);

        var top = new Rgba32(66, 133, 244, 255);
        var bottom = new Rgba32(26, 61, 160, 255);
        var inset = 10;
        var radius = 54;
        var box = new Rectangle(inset, inset, size - inset * 2, size - inset * 2);

        image.Mutate(ctx =>
        {
            for (var y = 0; y < box.Height; y++)
            {
                var t = y / (float)(box.Height - 1);
                var c = Lerp(top, bottom, t);
                ctx.Fill(c, new Rectangle(box.X, box.Y + y, box.Width, 1));
            }
        });

        using var lines = new Image<Rgba32>(box.Width, box.Height, Color.Transparent);
        var whiteSoft = new Rgba32(255, 255, 255, 70);
        var angle = 30.0 * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var diag = Math.Sqrt(2.0) * box.Width;
        var spacing = 56;
        var thickness = 14;
        var cx = box.Width / 2.0;
        var cy = box.Height / 2.0;

        lines.Mutate(ctx =>
        {
            for (var k = -(int)(diag / spacing) - 1; k <= (int)(diag / spacing) + 1; k++)
            {
                var offset = k * spacing;
                var px = cx - cos * diag / 2 - sin * offset;
                var py = cy - sin * diag / 2 + cos * offset;
                var p1 = new PointF((float)px, (float)py);
                var p2 = new PointF((float)(px + cos * diag), (float)(py + sin * diag));
                ctx.DrawLine(whiteSoft, thickness, p1, p2);
            }
        });

        var maskRadius = radius;
        lines.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (!InsideRoundedRect(x, y, box.Width, box.Height, maskRadius))
                    {
                        row[x] = Color.Transparent;
                    }
                }
            }
        });

        image.Mutate(ctx => ctx.DrawImage(lines, new Point(box.X, box.Y), 1f));

        DrawPhotoGlyph(image);

        return image;
    }

    private static void DrawPhotoGlyph(Image<Rgba32> image)
    {
        var white = new Rgba32(255, 255, 255, 255);
        var deep = new Rgba32(23, 55, 140, 255);

        var frame = new RectangleF(56, 74, 144, 110);
        image.Mutate(ctx => ctx.Fill(white, frame));

        var mountains = new Polygon(new LinearLineSegment(new[]
        {
            new PointF(56, 184),
            new PointF(96, 122),
            new PointF(128, 162),
            new PointF(152, 138),
            new PointF(200, 184),
        }));

        image.Mutate(ctx =>
        {
            ctx.Fill(deep, mountains);
            ctx.Fill(deep, new EllipsePolygon(152, 104, 13));
        });
    }

    private static bool InsideRoundedRect(int x, int y, int w, int h, int r)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        var cx = Math.Clamp(x, r, w - 1 - r);
        var cy = Math.Clamp(y, r, h - 1 - r);
        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= r * r;
    }

    private static Rgba32 Lerp(Rgba32 a, Rgba32 b, float t)
    {
        byte L(byte x, byte y) => (byte)Math.Clamp((int)Math.Round(x + (y - x) * t), 0, 255);
        return new Rgba32(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B), L(a.A, b.A));
    }

    private static byte[] EncodeBmpEntry(Image<Rgba32> image)
    {
        var w = image.Width;
        var h = image.Height;
        var xorSize = w * h * 4;
        var andRowBytes = ((w + 31) / 32) * 4;
        var andSize = andRowBytes * h;
        var total = 40 + xorSize + andSize;
        var data = new byte[total];

        var header = data.AsSpan(0, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[..4], 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], w);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], h * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[12..14], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..16], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..24], xorSize + andSize);

        var xor = data.AsMemory(40, xorSize);
        var and = data.AsMemory(40 + xorSize, andSize);

        image.ProcessPixelRows(accessor =>
        {
            var xorSpan = xor.Span;
            var andSpan = and.Span;
            for (var srcY = 0; srcY < accessor.Height; srcY++)
            {
                var srcRow = accessor.GetRowSpan(srcY);
                var dstY = h - 1 - srcY;
                var xorRow = xorSpan.Slice(dstY * w * 4, w * 4);
                var andRow = andSpan.Slice(dstY * andRowBytes, andRowBytes);
                andRow.Clear();

                for (var x = 0; x < srcRow.Length; x++)
                {
                    var p = srcRow[x];
                    xorRow[x * 4] = p.B;
                    xorRow[x * 4 + 1] = p.G;
                    xorRow[x * 4 + 2] = p.R;
                    xorRow[x * 4 + 3] = p.A;
                    if (p.A < 128)
                    {
                        andRow[x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }
        });

        return data;
    }

    private static byte[] BuildIco(List<(int Size, byte[] Data)> entries)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);

        var offset = 6 + entries.Count * 16;
        foreach (var (size, data) in entries)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)data.Length);
            writer.Write((uint)offset);
            offset += data.Length;
        }

        foreach (var (_, data) in entries)
        {
            writer.Write(data);
        }

        writer.Flush();
        return ms.ToArray();
    }
}
