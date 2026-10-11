using System.Text;

namespace ImageWatermarkPortable.Services;

public sealed class ErrorLogService : IDisposable
{
    private readonly string _logPath;
    private readonly object _lock = new();
    private StreamWriter? _writer;

    public ErrorLogService(string destinationRoot)
    {
        _logPath = Path.Combine(destinationRoot, "watermark-errors.log");
    }

    public void StartSession(string sourceRoot, string destinationRoot, int total)
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = new StreamWriter(new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8);
            _writer.WriteLine($"# Image Watermark Portable Error Log");
            _writer.WriteLine($"# Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writer.WriteLine($"# Source: {sourceRoot}");
            _writer.WriteLine($"# Destination: {destinationRoot}");
            _writer.WriteLine($"# Total: {total}");
            _writer.Flush();
        }
    }

    public void LogError(string relativePath, string stage, string message)
    {
        lock (_lock)
        {
            _writer?.WriteLine($"{DateTime.Now:HH:mm:ss} [{stage}] {relativePath} - {message}");
            _writer?.Flush();
        }
    }

    public void FinishSession(int succeeded, int failed, TimeSpan elapsed)
    {
        lock (_lock)
        {
            _writer?.WriteLine($"# Finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writer?.WriteLine($"# Succeeded: {succeeded}");
            _writer?.WriteLine($"# Failed: {failed}");
            _writer?.WriteLine($"# Elapsed: {elapsed}");
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
