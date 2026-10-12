using System.IO.Compression;
using System.Text.RegularExpressions;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// A test that needs a tar.exe with the 7z writer. Skipped where this system's tar.exe has none
/// (Pakko then refuses 7z creation, which Archiver.Core.Tests covers).
/// </summary>
public sealed class SevenZipWriterFactAttribute : FactAttribute
{
    public SevenZipWriterFactAttribute()
    {
        if (!new TarSandboxedService(new GroupPolicyOptions()).CanCreateSevenZipAsync().GetAwaiter().GetResult())
            Skip = "this system's tar.exe does not write 7z";
    }
}

/// <summary>
/// T-F342: the 7z archives tar.exe writes for Pakko, read by 7-Zip itself - Pakko's own reader is
/// the same tar.exe and proves nothing about the format. In this project for the vendored 7za.exe,
/// like ZipEntryWriterCompatibilityTests; correctness, not timing.
/// </summary>
public sealed partial class SevenZipCreationCompatibilityTests : IDisposable
{
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly CompressionLevel[] Levels =
        [CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize, CompressionLevel.NoCompression];

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [GeneratedRegex(@"Method = LZMA2?:(\d+)")]
    private static partial Regex LzmaDictionary();

    private string BuildTree()
    {
        string tree = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub folder"));
        Directory.CreateDirectory(Path.Combine(tree, "empty folder"));
        File.WriteAllText(Path.Combine(tree, "text.txt"), string.Concat(Enumerable.Repeat("compressible text ", 20_000)));
        byte[] random = new byte[300_000];
        new Random(7).NextBytes(random);
        File.WriteAllBytes(Path.Combine(tree, "sub folder", "random.bin"), random);
        File.WriteAllBytes(Path.Combine(tree, "sub folder", "-dash.txt"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(tree, "empty.txt"), []);
        return tree;
    }

    private async Task<string> CreateAsync(string source, string name, SevenZipMethod method, CompressionLevel level)
    {
        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = name,
            Format = ArchiveContainerFormat.SevenZip,
            SevenZipMethod = method,
            CompressionLevel = level,
        });
        result.Success.Should().BeTrue(because: $"{method}/{level}: " + string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.CreatedFiles.Should().ContainSingle().Which;
    }

    private static Dictionary<string, byte[]> Files(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    // --- Happy path ---

    [SevenZipWriterFact]
    public async Task EveryMethodAtEveryLevel_IsARealSevenZipWithTheMethodAskedFor()
    {
        string tree = BuildTree();

        foreach (SevenZipMethod method in Enum.GetValues<SevenZipMethod>())
        {
            foreach (CompressionLevel level in Levels)
            {
                string archive = await CreateAsync(tree, $"{method}-{level}", method, level);

                // "tar -cf name.7z" without --format writes ustar under that name.
                File.ReadAllBytes(archive).AsSpan(0, 6).SequenceEqual(Signature).Should().BeTrue(because: $"{method}/{level} must be a 7z");
                SevenZipRunner.Test(archive);
                string expected = level == CompressionLevel.NoCompression ? "Method = Copy" : method switch
                {
                    SevenZipMethod.Lzma2 => "Method = LZMA2:",
                    SevenZipMethod.Lzma => "Method = LZMA:",
                    SevenZipMethod.BZip2 => "Method = BZip2",
                    SevenZipMethod.Deflate => "Method = Deflate",
                    _ => "Method = Copy",
                };
                SevenZipRunner.ListTechnical(archive).Should().Contain(expected, because: $"{method}/{level}");
            }
        }
    }

    [SevenZipWriterFact]
    public async Task DefaultArchive_ExtractedBySevenZip_EqualsTheSource()
    {
        string tree = BuildTree();

        string archive = await CreateAsync(tree, "default", SevenZipMethod.Lzma2, CompressionLevel.Optimal);
        string extracted = Path.Combine(_temp.Path, "extracted");
        SevenZipRunner.Extract(archive, extracted);

        Files(Path.Combine(extracted, "tree")).Should().BeEquivalentTo(Files(tree));
        Directory.Exists(Path.Combine(extracted, "tree", "empty folder")).Should().BeTrue();
    }

    // --- Security & Boundary: the level reaches the writer ---

    [SevenZipWriterFact]
    public async Task Level_ChangesTheDictionaryTheWriterUses()
    {
        string tree = BuildTree();

        foreach (SevenZipMethod method in new[] { SevenZipMethod.Lzma2, SevenZipMethod.Lzma })
        {
            int Dictionary(string archive) => int.Parse(
                LzmaDictionary().Match(SevenZipRunner.ListTechnical(archive)).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            int fastest = Dictionary(await CreateAsync(tree, $"{method}-fast", method, CompressionLevel.Fastest));
            int smallest = Dictionary(await CreateAsync(tree, $"{method}-small", method, CompressionLevel.SmallestSize));

            fastest.Should().BeLessThan(smallest, because: $"{method}: level 1 and level 9 use different dictionaries");
        }
    }

    // --- Misuse: the Copy method, whatever level is asked ---

    [SevenZipWriterFact]
    public async Task CopyMethod_StoresTheBytesUnchanged()
    {
        string tree = BuildTree();

        string archive = await CreateAsync(tree, "copy", SevenZipMethod.Copy, CompressionLevel.SmallestSize);

        new FileInfo(archive).Length.Should().BeGreaterThan(Files(tree).Values.Sum(bytes => (long)bytes.Length));
        SevenZipRunner.Test(archive);
    }

    // --- Error path / known limits of libarchive's writer, pinned ---

    [SevenZipWriterFact]
    public async Task Attributes_ReadOnlyIsKept_HiddenIsLost()
    {
        string tree = Path.Combine(_temp.Path, "attrs");
        Directory.CreateDirectory(tree);
        string readOnly = Path.Combine(tree, "readonly.txt");
        string hidden = Path.Combine(tree, "hidden.txt");
        File.WriteAllText(readOnly, "r");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        try
        {
            string archive = await CreateAsync(tree, "attrs", SevenZipMethod.Lzma2, CompressionLevel.Optimal);

            string[] blocks = SevenZipRunner.ListTechnical(archive).ReplaceLineEndings("\n").Split("\n\n");
            string Attributes(string name) => blocks.Single(block => block.Contains(@"Path = attrs\" + name, StringComparison.Ordinal))
                .Split('\n').Single(line => line.StartsWith("Attributes = ", StringComparison.Ordinal))["Attributes = ".Length..];

            Attributes("readonly.txt").Split(' ')[0].Should().Contain("R");
            Attributes("hidden.txt").Split(' ')[0].Should().NotContain("H");
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }
}
