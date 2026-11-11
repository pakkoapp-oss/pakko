using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.Litmus;

/// <summary>What one measured run gets: its input shape, a fresh output folder, the services, and
/// whatever <see cref="Operation.Prepare"/> left for it.</summary>
internal sealed class RunContext(Shape shape, string output, PakkoServices services)
{
    public Shape Shape { get; } = shape;
    public string Output { get; } = output;
    public PakkoServices Services { get; } = services;
    public string? Prepared { get; set; }
}

/// <summary>
/// One row of the litmus run. <see cref="Prepare"/> is not measured. <see cref="Run"/> returns
/// null when it ran, or why it was skipped on this machine or shape; a failed operation throws.
/// </summary>
internal sealed record Operation(string Name, Func<RunContext, Task<string?>> Run, Func<RunContext, Task>? Prepare = null);

internal static class Operations
{
    private const int FloorBufferSize = 80 * 1024;

    public static readonly IReadOnlyList<Operation> All =
    [
        new("noop", _ => Task.FromResult<string?>(null)),
        new("floor-read", FloorRead),
        new("floor-write", FloorWrite),

        new("zip-optimal", c => Zip(c, CompressionLevel.Optimal)),
        new("zip-fastest", c => Zip(c, CompressionLevel.Fastest)),
        new("zip-store", c => Zip(c, CompressionLevel.NoCompression)),
        new("zip-smallest", c => Zip(c, CompressionLevel.SmallestSize)),
        new("zip-password", c => Zip(c, CompressionLevel.Optimal, password: true)),
        new("zip-separate-optimal", c => Separate(c, CompressionLevel.Optimal)),
        new("zip-separate-fastest", c => Separate(c, CompressionLevel.Fastest)),
        new("zip-recovery", c => Zip(c, CompressionLevel.Optimal, recovery: true)),

        new("zip-extract", c => Extract(c, c.Shape.Zip)),
        new("zip-extract-password", c => Extract(c, c.Shape.ZipWithPassword, password: true)),
        new("zip-test", c => Test(c, c.Shape.Zip)),
        new("zip-list", c => List(c, c.Shape.Zip)),
        new("zip-preview", Preview, PreparePreview),
        new("zip-recovery-verify", c => Test(c, c.Shape.ZipWithRecovery, verifyRecoveryData: true)),
        new("zip-recovery-repair", Repair, PrepareRepair),
        new("zip-scan", Scan),

        new("targz-create", TarGzCreate),
        new("targz-extract", c => c.Shape.TarGz is null ? NoTar() : Extract(c, c.Shape.TarGz)),
        new("targz-test", c => c.Shape.TarGz is null ? NoTar() : Test(c, c.Shape.TarGz)),
        new("targz-list", c => c.Shape.TarGz is null ? NoTar() : List(c, c.Shape.TarGz)),
        new("sevenzip-create", SevenZipCreate),
        new("gz-test", c => c.Shape.Gz is null ? Task.FromResult<string?>("one file only") : Test(c, c.Shape.Gz)),

        new("hash-crc32", c => Hash(c, HashAlgorithmKind.Crc32)),
        new("hash-sha256", c => Hash(c, HashAlgorithmKind.Sha256)),
    ];

    private static Task<string?> NoTar() => Task.FromResult<string?>("no tar.gz fixture (tar.exe missing or blocked)");

    private static IEnumerable<string> SourceFiles(Shape shape) =>
        File.Exists(shape.Source) ? [shape.Source] : Directory.EnumerateFiles(shape.Source, "*", SearchOption.AllDirectories);

    // The floor the file system costs: the same files read once, and files of the same sizes written.
    private static async Task<string?> FloorRead(RunContext c)
    {
        byte[] buffer = new byte[FloorBufferSize];
        foreach (string file in SourceFiles(c.Shape))
        {
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.SequentialScan);
            while (await input.ReadAsync(buffer) > 0) { /* reading is the measurement */ }
        }
        return null;
    }

    private static async Task<string?> FloorWrite(RunContext c)
    {
        byte[] buffer = new byte[FloorBufferSize];
        Random.Shared.NextBytes(buffer);
        int index = 0;
        foreach (string file in SourceFiles(c.Shape))
        {
            long length = new FileInfo(file).Length;
            await using var output = new FileStream(Path.Combine(c.Output, $"w{index++:D6}.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 0);
            for (long written = 0; written < length; written += buffer.Length)
                await output.WriteAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - written)));
        }
        return null;
    }

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> GivePassword() =>
        _ => Task.FromResult(new PasswordDecision { Password = Fixtures.Password });

    private static async Task<string?> Zip(RunContext c, CompressionLevel level, bool password = false, bool recovery = false)
    {
        Expect(await c.Services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [c.Shape.Source], DestinationFolder = c.Output, ArchiveName = "out", CompressionLevel = level,
            ResolvePasswordAsync = password ? GivePassword() : null,
            RecoveryPercent = recovery ? 5 : 0,
        }));
        return null;
    }

    private static async Task<string?> Separate(RunContext c, CompressionLevel level)
    {
        if (File.Exists(c.Shape.Source))
            return "many files only";
        string[] files = [.. SourceFiles(c.Shape)];
        Expect(await c.Services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = files, DestinationFolder = c.Output, Mode = ArchiveMode.SeparateArchives, CompressionLevel = level,
        }));
        return null;
    }

    private static async Task<string?> Extract(RunContext c, string archive, bool password = false, IReadOnlyList<string>? selected = null)
    {
        Interfaces.IExtractionRouter router = await c.Services.CreateExtractionRouterAsync();
        Expect(await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = c.Output, Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = password ? GivePassword() : null,
            SelectedEntryPaths = selected,
            ApplyDownloadMark = false,
        }));
        return null;
    }

    private static async Task<string?> Test(RunContext c, string archive, bool verifyRecoveryData = false)
    {
        Interfaces.IExtractionRouter router = await c.Services.CreateExtractionRouterAsync();
        Expect(await router.TestAsync([archive], verifyRecoveryData: verifyRecoveryData));
        return null;
    }

    private static async Task<string?> List(RunContext c, string archive)
    {
        Interfaces.IArchiveListingRouter router = await c.Services.CreateListingRouterAsync();
        ArchiveListResult listed = await router.ListEntriesAsync(archive);
        if (!listed.Success || listed.Entries.Count == 0)
            throw new InvalidOperationException($"Listing failed: {listed.ErrorMessage}");
        return null;
    }

    // The App's preview: one entry extracted on its own (MainViewModel.PreviewBrowserEntryAsync).
    private static async Task PreparePreview(RunContext c)
    {
        Interfaces.IArchiveListingRouter router = await c.Services.CreateListingRouterAsync();
        ArchiveListResult listed = await router.ListEntriesAsync(c.Shape.Zip);
        c.Prepared = listed.Entries.Last(e => !e.IsDirectory).Path;
    }

    private static Task<string?> Preview(RunContext c) => Extract(c, c.Shape.Zip, selected: [c.Prepared!]);

    // A copy of the archive with recovery data, damaged in the middle.
    private static Task PrepareRepair(RunContext c)
    {
        string from = Path.GetDirectoryName(c.Shape.ZipWithRecovery)!;
        string to = Path.Combine(c.Output, "damaged");
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        string copy = Path.Combine(to, Path.GetFileName(c.Shape.ZipWithRecovery));
        using (var stream = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = stream.Length / 2;
            stream.Write(new byte[Math.Max(1, stream.Length / 50)]); // 2 %, within the 5 % of recovery data
        }
        c.Prepared = copy;
        return Task.CompletedTask;
    }

    private static async Task<string?> Repair(RunContext c)
    {
        Expect(await c.Services.RecoveryService.RepairAsync(new RepairOptions
        {
            Paths = [c.Prepared!], OutputDirectory = Path.Combine(c.Output, "repaired"), ApplyDownloadMark = false,
        }));
        return null;
    }

    private static async Task<string?> Scan(RunContext c)
    {
        Interfaces.IAntivirusScanService scanner = await c.Services.CreateScanServiceAsync();
        await scanner.ScanAsync(new AntivirusScanOptions { ArchivePaths = [c.Shape.Zip] });
        return null;
    }

    private static async Task<string?> TarGzCreate(RunContext c)
    {
        if (c.Shape.TarGz is null)
            return "no tar.gz fixture (tar.exe missing or blocked)";
        Expect(await c.Services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [c.Shape.Source], DestinationFolder = c.Output, ArchiveName = "out", Format = ArchiveContainerFormat.TarGz,
        }));
        return null;
    }

    private static async Task<string?> SevenZipCreate(RunContext c)
    {
        if (!await c.Services.TarService.CanCreateSevenZipAsync())
            return "this tar.exe cannot write 7z";
        Expect(await c.Services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [c.Shape.Source], DestinationFolder = c.Output, ArchiveName = "out", Format = ArchiveContainerFormat.SevenZip,
        }));
        return null;
    }

    private static async Task<string?> Hash(RunContext c, HashAlgorithmKind algorithm)
    {
        HashResult result = await FileHashService.ComputeAsync([c.Shape.Source], algorithm, progress: null, CancellationToken.None);
        if (result.Entries.FirstOrDefault(e => e.Error is not null) is { } failed)
            throw new InvalidOperationException($"Hashing failed: {failed.SourcePath}: {failed.Error}");
        return null;
    }

    private static void Expect(ArchiveResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.SourcePath}: {e.Message}")));
    }
}
