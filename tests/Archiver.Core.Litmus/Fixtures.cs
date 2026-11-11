using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.Litmus;

/// <summary>
/// One input shape and the archives built from it. <see cref="Source"/> is what an operation is
/// given: the folder for many files, the file itself for one large one.
/// </summary>
internal sealed record Shape(
    string Name, string Folder, string Source, int Files, long Bytes,
    string Zip, string ZipWithPassword, string ZipWithRecovery, string? TarGz, string? Gz);

/// <summary>
/// The fixtures, seeded and built once per scale under %TEMP% (T-F318's generator: words with a
/// random byte in seven, 1-10 KB per file). Kept between runs: a 256 MiB file and its archives
/// take a while, and the counters must not include building them.
/// </summary>
internal static class Fixtures
{
    public const string Password = "litmus-password";
    private const string Words = "alpha beta gamma delta archive entry deflate zlib window pool worker ";

    public static readonly string[] Measured = ["many", "one"];

    public static string RootFor(int scale) =>
        Path.Combine(Path.GetTempPath(), "pakko-litmus", $"fixtures-v2-x{scale}");

    /// <summary>The shape's warm-up twin: the same operation on a small input first, so JIT and
    /// one-time state are not counted.</summary>
    public static string MiniOf(string shape) => "mini-" + shape;

    public static async Task<Dictionary<string, Shape>> PrepareAsync(string root, int scale, PakkoServices services)
    {
        var shapes = new Dictionary<string, Shape>(StringComparer.Ordinal);
        foreach ((string name, int files, long oneFileBytes) in new (string, int, long)[]
        {
            ("many", 2000 * scale, 0), ("one", 1, 256L * 1024 * 1024),
            ("mini-many", 20, 0), ("mini-one", 1, 1024L * 1024),
        })
        {
            shapes[name] = await PrepareShapeAsync(Path.Combine(root, name), name, files, oneFileBytes, services);
        }
        return shapes;
    }

    private static async Task<Shape> PrepareShapeAsync(string folder, string name, int files, long oneFileBytes, PakkoServices services)
    {
        string ready = Path.Combine(folder, "ready.txt");
        string src = Path.Combine(folder, "src");
        if (!File.Exists(ready))
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(src);
            if (oneFileBytes > 0)
                WriteLargeFile(Path.Combine(src, "big.bin"), oneFileBytes);
            else
                WriteManyFiles(src, files);
            await BuildArchivesAsync(folder, oneFileBytes > 0 ? Path.Combine(src, "big.bin") : src, services);
            await File.WriteAllTextAsync(ready, DateTime.UtcNow.ToString("O"));
        }

        string source = oneFileBytes > 0 ? Path.Combine(src, "big.bin") : src;
        long bytes = oneFileBytes > 0
            ? new FileInfo(source).Length
            : Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        string? tarGz = Path.Combine(folder, "t.tar.gz");
        string? gz = Path.Combine(folder, "big.bin.gz");
        return new Shape(name, folder, source, files, bytes,
            Path.Combine(folder, "a.zip"), Path.Combine(folder, "pw.zip"), Path.Combine(folder, "rec", "r.zip"),
            File.Exists(tarGz) ? tarGz : null, File.Exists(gz) ? gz : null);
    }

    private static void WriteManyFiles(string src, int files)
    {
        var rng = new Random(7);
        int perFolder = Math.Max(1, files / 20);
        for (int i = 0; i < files; i++)
        {
            string dir = Path.Combine(src, $"d{i / perFolder:D2}");
            Directory.CreateDirectory(dir);
            byte[] data = new byte[rng.Next(1024, 10241)];
            Fill(data, rng);
            File.WriteAllBytes(Path.Combine(dir, $"f{i:D6}.txt"), data);
        }
    }

    private static void WriteLargeFile(string path, long length)
    {
        var rng = new Random(11);
        byte[] block = new byte[1024 * 1024];
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024);
        for (long written = 0; written < length; written += block.Length)
        {
            Fill(block, rng);
            output.Write(block, 0, (int)Math.Min(block.Length, length - written));
        }
    }

    private static void Fill(byte[] data, Random rng)
    {
        for (int j = 0; j < data.Length; j++)
            data[j] = j % 7 == 0 ? (byte)rng.Next(256) : (byte)Words[rng.Next(Words.Length)];
    }

    private static async Task BuildArchivesAsync(string folder, string source, PakkoServices services)
    {
        await Expect(services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = folder, ArchiveName = "a",
        }), "the zip fixture");
        await Expect(services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = folder, ArchiveName = "pw",
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = Password }),
        }), "the password zip fixture");
        string withRecovery = Path.Combine(folder, "rec");
        Directory.CreateDirectory(withRecovery);
        await Expect(services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = withRecovery, ArchiveName = "r", RecoveryPercent = 5,
        }), "the zip with recovery data");
        if (!Directory.EnumerateFiles(withRecovery, "*.par2").Any())
            throw new InvalidOperationException($"The zip with recovery data in {withRecovery} has no PAR2 files.");

        // tar.exe may be missing or blocked: the tar rows are then skipped, not failed.
        ArchiveResult tar = await services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = folder, ArchiveName = "t", Format = ArchiveContainerFormat.TarGz,
        });
        if (!tar.Success)
            Console.Error.WriteLine($"no tar.gz fixture in {folder}: {string.Join("; ", tar.Errors.Select(e => e.Message))}");

        if (File.Exists(source))
        {
            using FileStream input = File.OpenRead(source);
            using FileStream output = File.Create(Path.Combine(folder, Path.GetFileName(source) + ".gz"));
            using var gzip = new GZipStream(output, CompressionLevel.Optimal);
            await input.CopyToAsync(gzip);
        }
    }

    private static async Task Expect(Task<ArchiveResult> run, string what)
    {
        ArchiveResult result = await run;
        if (!result.Success)
            throw new InvalidOperationException($"Could not build {what}: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }
}
