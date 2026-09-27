using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F225: Pakko's folder DataSum/NamesSum against the vendored 7-Zip's own <c>7za h</c> on the
/// same folder, live — the oracle, not values copied from it once. Covers the cases that used to
/// diverge: the root folder counted as an item, nested subfolders, an empty subfolder, a
/// one-file folder (whose DataSum 7-Zip prints without a suffix), and a non-ASCII name.
/// A correctness suite, not a timing one (see ZipEntryWriterCompatibilityTests).
/// </summary>
public sealed class FolderHashParityTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Folder(string name, params (string RelativePath, string? Content)[] items)
    {
        string root = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(root);
        foreach ((string relativePath, string? content) in items)
        {
            string full = Path.Combine(root, relativePath);
            if (content is null)
            {
                Directory.CreateDirectory(full);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }

    public static TheoryData<string> Algorithms => new() { "CRC32", "SHA256" };

    private static async Task AssertMatchesSevenZip(string folder, string algorithm)
    {
        HashAlgorithmKind kind = algorithm == "CRC32" ? HashAlgorithmKind.Crc32 : HashAlgorithmKind.Sha256;
        (string expectedData, string expectedNames) = SevenZipRunner.HashSums(folder, algorithm);

        HashResult result = await FileHashService.ComputeAsync([folder], kind, null, CancellationToken.None);

        result.Folder!.DataSum.Should().Be(expectedData);
        result.Folder.NamesSum.Should().Be(expectedNames);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public Task OneFileFolder_MatchesSevenZip(string algorithm) =>
        AssertMatchesSevenZip(Folder("one", ("a.txt", "hello world")), algorithm);

    [Theory]
    [MemberData(nameof(Algorithms))]
    public Task FlatFolder_MatchesSevenZip(string algorithm) =>
        AssertMatchesSevenZip(Folder("flat", ("a.txt", "hello world"), ("b.txt", "second file content here")), algorithm);

    [Theory]
    [MemberData(nameof(Algorithms))]
    public Task NestedFolderWithEmptyAndNonAsciiNames_MatchesSevenZip(string algorithm) =>
        AssertMatchesSevenZip(Folder("nest",
            ("z.txt", "zzz"),
            ("B/b1.txt", "b1"),
            ("B/x/deep.txt", "deep"),
            ("a/a1.txt", "a1"),
            ("a/empty", null),
            ("дані/файл.txt", "cyrillic")), algorithm);

    [Theory]
    [MemberData(nameof(Algorithms))]
    public Task EmptyFolder_MatchesSevenZip(string algorithm) =>
        AssertMatchesSevenZip(Folder("emptyf"), algorithm);

    // 7-Zip names the folder as it is on disk ("one"), not as typed ("ONE").
    [Theory]
    [MemberData(nameof(Algorithms))]
    public Task FolderTypedInOtherCase_MatchesSevenZip(string algorithm)
    {
        string folder = Folder("casing", ("a.txt", "hello world"), ("sub/b.txt", "b"));
        return AssertMatchesSevenZip(Path.Combine(Path.GetDirectoryName(folder)!, "CASING"), algorithm);
    }

    // "folder\." hashes only the contents: no folder item, no name prefix.
    [Theory]
    [MemberData(nameof(Algorithms))]
    public Task FolderContentsViaDot_MatchesSevenZip(string algorithm)
    {
        string folder = Folder("dot", ("a.txt", "hello world"), ("sub/b.txt", "b"));
        return AssertMatchesSevenZip(Path.Combine(folder, "."), algorithm);
    }
}
