using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;

// T-F132 empirical spike: a minimal worker whose only job is running ZipArchiveService.ArchiveAsync/
// ExtractAsync inside a sandboxed process, so tests/Archiver.Core.PerformanceTests can time the real
// overhead of a sandboxed launch against the in-process baseline. Never wired into
// ExtractionRouter/ArchiveCreationRouter — deliberately bypasses the tar.exe capability-detection
// spawn those add, so the measured number isolates ZIP-sandboxing overhead specifically, not an
// unrelated tar.exe check. See docs/DECISIONS.md's T-F132 entry.

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: ZipSandboxSpike.exe archive <sourceDir> <destZipPath>");
    Console.Error.WriteLine("       ZipSandboxSpike.exe extract <archiveZipPath> <destDir>");
    return 2;
}

string operation = args[0];
string sourcePath = args[1];
string destPath = args[2];

var svc = new ZipArchiveService(policy: null);
var stopwatch = Stopwatch.StartNew();

ArchiveResult result;
try
{
    result = operation switch
    {
        "archive" => await svc.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourcePath],
            DestinationFolder = Path.GetDirectoryName(destPath)!,
            ArchiveName = Path.GetFileNameWithoutExtension(destPath),
        }),
        "extract" => await svc.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [sourcePath],
            DestinationFolder = destPath,
            Mode = ExtractMode.SingleFolder,
        }),
        _ => throw new ArgumentException($"unknown operation '{operation}'"),
    };
}
catch (Exception ex)
{
    stopwatch.Stop();
    Console.Error.WriteLine($"internal_elapsed_ms={stopwatch.Elapsed.TotalMilliseconds}");
    Console.Error.WriteLine($"error: unhandled exception: {ex}");
    return 1;
}

stopwatch.Stop();
Console.Error.WriteLine($"internal_elapsed_ms={stopwatch.Elapsed.TotalMilliseconds}");

if (!result.Success || result.Errors.Count > 0)
{
    foreach (ArchiveError e in result.Errors)
        Console.Error.WriteLine($"error: {e.SourcePath}: {e.Message}");
    return 1;
}

return 0;
