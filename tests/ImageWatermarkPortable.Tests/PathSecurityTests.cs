using ImageWatermarkPortable.Services;

namespace ImageWatermarkPortable.Tests;

public sealed class PathSecurityTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "iwp-tests", Guid.NewGuid().ToString("N"));

    private static string MakeDir(params string[] parts)
    {
        var path = Path.Combine(Root, Path.Combine(parts));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Rejects_SameDirectory()
    {
        var dir = MakeDir("same");
        Assert.False(PathSecurity.TryValidatePaths(dir, dir, out var error));
        Assert.Contains("相同", error);
    }

    [Fact]
    public void Rejects_DestinationInsideSource()
    {
        var src = MakeDir("outer");
        var dest = Path.Combine(src, "inner");
        Directory.CreateDirectory(dest);

        Assert.False(PathSecurity.TryValidatePaths(src, dest, out var error));
        Assert.Contains("目的", error);
    }

    [Fact]
    public void Rejects_SourceInsideDestination()
    {
        var dest = MakeDir("outer2");
        var src = Path.Combine(dest, "inner2");
        Directory.CreateDirectory(src);

        Assert.False(PathSecurity.TryValidatePaths(src, dest, out var error));
        Assert.Contains("來源", error);
    }

    [Fact]
    public void Rejects_SameDirectory_DifferentCaseAndTrailingSlash()
    {
        var src = MakeDir("CaseDir");
        var alias = src.ToUpperInvariant() + Path.DirectorySeparatorChar;

        Assert.False(PathSecurity.TryValidatePaths(src, alias, out _));
    }

    [Fact]
    public void Accepts_SiblingDirectories()
    {
        var src = MakeDir("srcA");
        var dest = MakeDir("destA");

        Assert.True(PathSecurity.TryValidatePaths(src, dest, out var error), error);
    }

    [Fact]
    public void Rejects_MissingSource()
    {
        var src = Path.Combine(Root, "does-not-exist");
        var dest = MakeDir("destB");

        Assert.False(PathSecurity.TryValidatePaths(src, dest, out var error));
        Assert.Contains("不存在", error);
    }

    [Fact]
    public void Rejects_BlankInputs()
    {
        Assert.False(PathSecurity.TryValidatePaths("", " ", out _));
    }

    [Fact]
    public void IsPathInsideRoot_UsesDirectoryBoundary()
    {
        var root = MakeDir("root");
        Assert.True(PathSecurity.IsPathInsideRoot(root, Path.Combine(root, "a", "b.jpg")));
        Assert.False(PathSecurity.IsPathInsideRoot(root, root + "x"));
    }
}
