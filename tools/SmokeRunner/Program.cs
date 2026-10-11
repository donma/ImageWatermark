using System.Diagnostics;
using ImageWatermarkPortable.Models;
using ImageWatermarkPortable.Services;

var source = args[0];
var dest = args[1];
var text = args.Length > 2 ? args[2] : "Smoke Test 測試";
var opacity = args.Length > 3 ? int.Parse(args[3]) : 30;
var fontSize = args.Length > 4 ? int.Parse(args[4]) : 36;

if (!PathSecurity.TryValidatePaths(source, dest, out var error))
{
    Console.Error.WriteLine($"PATH ERROR: {error}");
    return 1;
}

var discovery = new ImageDiscoveryService();
var files = discovery.EnumerateImages(source);
Console.WriteLine($"Discovered: {files.Count} files");
foreach (var f in files.Take(20))
{
    Console.WriteLine($"  {Path.GetRelativePath(source, f)}");
}

var options = new WatermarkOptions { Text = text, OpacityPercent = opacity, FontSizePx = fontSize };
var batch = new BatchProcessingService(new WatermarkRenderer(), new ImageEncoderService(), new SafeFileWriter());
var progress = new SynchronousProgress();

var sw = Stopwatch.StartNew();
var result = await batch.ProcessAsync(files, source, dest, options, progress, CancellationToken.None);
sw.Stop();

Console.WriteLine($"Total={result.Total} Succeeded={result.Succeeded} Failed={result.Failed} Elapsed={result.Elapsed}");
Console.WriteLine($"LastProgress: processed={progress.Last?.Processed} percent={progress.Last?.Percent}");

var outputs = Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : Array.Empty<string>();
Console.WriteLine($"Output files: {outputs.Length}");
foreach (var o in outputs.Take(20))
{
    Console.WriteLine($"  {Path.GetRelativePath(dest, o)}  {new FileInfo(o).Length} bytes");
}

var log = Path.Combine(dest, "watermark-errors.log");
Console.WriteLine(File.Exists(log) ? $"Log exists ({new FileInfo(log).Length} bytes)" : "Log MISSING");

return result.Failed == 0 && result.Succeeded > 0 ? 0 : 2;

sealed class SynchronousProgress : IProgress<BatchProgress>
{
    public BatchProgress? Last { get; private set; }
    public void Report(BatchProgress value) => Last = value;
}
