using System.Collections.Generic;

namespace ImageWatermarkPortable.Services;

public sealed class ImageDiscoveryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public IReadOnlyList<string> EnumerateImages(string sourceRoot, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceRoot)) return Array.Empty<string>();

        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(sourceRoot);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(dir);
                if (IsReparsePoint(dirInfo))
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file);
                if (!SupportedExtensions.Contains(ext)) continue;

                try
                {
                    var fi = new FileInfo(file);
                    if (IsReparsePoint(fi)) continue;
                }
                catch
                {
                    continue;
                }

                results.Add(file);
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                try
                {
                    var di = new DirectoryInfo(sub);
                    if (IsReparsePoint(di)) continue;
                    stack.Push(sub);
                }
                catch
                {
                    // skip
                }
            }
        }

        return results;
    }

    public string? FindFirstValidImage(string sourceRoot, CancellationToken cancellationToken = default)
    {
        foreach (var file in EnumerateImages(sourceRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var fs = File.OpenRead(file);
                using var img = SixLabors.ImageSharp.Image.Load(fs);
                return file;
            }
            catch
            {
                // try next
            }
        }
        return null;
    }

    public static bool IsSupportedFile(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path));

    private static bool IsReparsePoint(FileSystemInfo fsi)
        => (fsi.Attributes & FileAttributes.ReparsePoint) != 0;
}
