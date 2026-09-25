using System.IO.Compression;
using System.Security.Cryptography;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F193 phase 2: the vendored 7za.exe as an independent oracle for Pakko's WinZip AES writer. A
/// Pakko-only round trip cannot catch a bug the writer and reader share (for example a CTR counter
/// with the wrong byte order), so every Pakko-written archive is extracted by 7-Zip and compared
/// byte for byte, and a 7-Zip-written archive goes the other way. Passwords are printable ASCII
/// only: 7-Zip refuses anything else when creating a ZIP and decodes it through the ANSI code
/// page when reading, which is why Pakko rejects non-ASCII passwords too (T-F193).
/// Correctness, not timing — lives here only to reuse the vendored binary (see TESTING.md).
/// </summary>
public sealed class ZipEncryptionCompatibilityTests : IDisposable
{
    private const string Password = "s3cret-pass";
    private const string SymbolPassword = "p@ss w0rd! ~`{}[]%^&";
    private static readonly string LongestPassword = new('x', 99);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> FixedPassword(string password) =>
        _ => Task.FromResult(new PasswordDecision { Password = password });

    // Covers every writer path: in-memory (small), temp-file (> 1 MiB), empty (Stored AES),
    // a directory placeholder, and a non-ASCII entry name (UTF-8 flag alongside the AES extra).
    private string BuildSourceTree()
    {
        string root = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(root, "empty-dir"));
        File.WriteAllText(Path.Combine(root, "small.txt"), string.Concat(Enumerable.Repeat("compressible ", 500)));
        File.WriteAllBytes(Path.Combine(root, "empty.bin"), []);
        File.WriteAllText(Path.Combine(root, "привіт.txt"), "кирилиця");
        byte[] big = new byte[(int)(1.5 * 1024 * 1024)];
        RandomNumberGenerator.Fill(big);
        File.WriteAllBytes(Path.Combine(root, "big.bin"), big);
        return root;
    }

    private static void AssertSameFiles(string expectedRoot, string actualRoot)
    {
        var expected = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(expectedRoot, p)).Order(StringComparer.Ordinal).ToList();
        var actual = Directory.GetFiles(actualRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(actualRoot, p)).Order(StringComparer.Ordinal).ToList();
        actual.Should().Equal(expected);
        foreach (string relative in expected)
            File.ReadAllBytes(Path.Combine(actualRoot, relative)).Should().Equal(
                File.ReadAllBytes(Path.Combine(expectedRoot, relative)), relative);
    }

    private async Task<string> ArchiveWithPakkoAsync(string source, string password, CompressionLevel level)
    {
        var result = await new ZipArchiveService().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, "pakko-out"),
            ArchiveName = "pakko",
            CompressionLevel = level,
            ResolvePasswordAsync = FixedPassword(password),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.CreatedFiles.Single();
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal, Password)]
    [InlineData(CompressionLevel.NoCompression, Password)]
    [InlineData(CompressionLevel.Optimal, SymbolPassword)]
    [InlineData(CompressionLevel.Optimal, null)]
    public async Task PakkoWrittenAesArchive_SevenZipTestsAndExtractsByteExact(CompressionLevel level, string? password)
    {
        password ??= LongestPassword;
        if (!SevenZipRunner.IsAvailable) return; // defense-in-depth only, see SevenZipRunner

        string source = BuildSourceTree();
        string archive = await ArchiveWithPakkoAsync(source, password, level);

        var test = () => SevenZipRunner.TestEncrypted(archive, password);
        test.Should().NotThrow("7-Zip must accept Pakko's AES headers and authentication codes");

        string sevenZipOut = Path.Combine(_temp.Path, "7z-out");
        SevenZipRunner.ExtractEncrypted(archive, sevenZipOut, password);
        AssertSameFiles(source, Path.Combine(sevenZipOut, "src"));
        Directory.Exists(Path.Combine(sevenZipOut, "src", "empty-dir")).Should().BeTrue();
    }

    [Fact]
    public async Task PakkoWrittenAesArchive_SevenZipRejectsWrongPassword()
    {
        if (!SevenZipRunner.IsAvailable) return;

        string archive = await ArchiveWithPakkoAsync(BuildSourceTree(), Password, CompressionLevel.Optimal);

        var test = () => SevenZipRunner.TestEncrypted(archive, "wrong-password");
        test.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task SevenZipWrittenAesArchiveWithSymbolPassword_PakkoExtractsByteExact()
    {
        if (!SevenZipRunner.IsAvailable) return;

        string source = BuildSourceTree();
        string archive = Path.Combine(_temp.Path, "7z.zip");
        SevenZipRunner.ArchiveEncrypted(archive, SymbolPassword, store: false, source);
        string destDir = Path.Combine(_temp.Path, "pakko-extract");

        var result = await new ZipArchiveService().ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(SymbolPassword),
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        AssertSameFiles(source, Path.Combine(destDir, Path.GetFileName(source))); // SingleFolder keeps the root folder (T-F205)
    }
}
