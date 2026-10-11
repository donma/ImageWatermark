namespace ImageWatermarkPortable.Models;

public sealed class BatchProgress : IDisposable
{
    public int Total { get; init; }
    public int Processed { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public string? CurrentFile { get; init; }
    public Bitmap? Preview { get; init; }
    public int Percent => Total <= 0 ? 0 : (int)Math.Round(Processed * 100.0 / Total);

    public void Dispose()
    {
        Preview?.Dispose();
    }
}
