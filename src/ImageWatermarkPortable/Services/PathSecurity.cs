namespace ImageWatermarkPortable.Services;

public static class PathSecurity
{
    public static bool TryValidatePaths(string sourceRoot, string destinationRoot, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(destinationRoot))
        {
            error = "來源與目的資料夾不可為空白。";
            return false;
        }

        string src;
        string dst;
        try
        {
            src = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            dst = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex)
        {
            error = $"路徑格式無效: {ex.Message}";
            return false;
        }

        string srcNoSlash = src.TrimEnd(Path.DirectorySeparatorChar);
        string dstNoSlash = dst.TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(srcNoSlash, dstNoSlash, StringComparison.OrdinalIgnoreCase))
        {
            error = "來源與目的資料夾不可相同。";
            return false;
        }

        if (dst.StartsWith(src, StringComparison.OrdinalIgnoreCase))
        {
            error = "目的資料夾不可位於來源資料夾內。";
            return false;
        }

        if (src.StartsWith(dst, StringComparison.OrdinalIgnoreCase))
        {
            error = "來源資料夾不可位於目的資料夾內。";
            return false;
        }

        if (!Directory.Exists(srcNoSlash))
        {
            error = "來源資料夾不存在。";
            return false;
        }

        return true;
    }

    public static bool IsPathInsideRoot(string root, string candidate)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidateFull = Path.GetFullPath(candidate);
        return candidateFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }
}
