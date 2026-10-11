namespace ImageWatermarkPortable.Services;

public sealed class SafeFileWriter
{
    public string GetTempPathFor(string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destination directory missing.");
        var name = Path.GetFileName(destinationPath);
        var tempName = $".{name}.{Guid.NewGuid():N}.tmp";
        return Path.Combine(dir, tempName);
    }

    public void ReplaceFile(string tempPath, string destinationPath)
    {
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    public void CleanupTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
