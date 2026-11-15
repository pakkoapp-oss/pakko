using System.IO.Compression;
using System.Security.Cryptography;
using Archiver.Core.Services.Zip;
using Archiver.Core.Tests.Fuzz;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Services.Zip.Legacy;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

// T-F529: the differential oracle. ZipCatalog (one walk of the directory, .NET's reader one batch at a
// time) against the reader it replaced (LegacyZipArchiveReader, a frozen copy: Pakko's names paired with
// .NET's reader of the whole file): every fixture archive and mutations of each must give the same
// verdict, the same names, collisions, sizes and CRCs, and the same bytes or the same error per entry.
// The differences that are meant are listed with why; any other one fails here.
public sealed class ZipReaderOracleTests : IDisposable
{
    private static readonly ZipNameCodePages Us = ZipNameCodePages.FromCodePages(437, 1252);
    private const int MutationsPerSeed = 40;
    private readonly string _folder = Directory.CreateTempSubdirectory("pakko-oracle-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void EveryFixtureAndItsMutations_ReadAsTheOldReaderReadThem()
    {
        var differences = new List<string>();
        int compared = 0;
        foreach (string seed in Directory.GetFiles(FixtureHelper.ArchivesDir, "*.zip").Order(StringComparer.Ordinal))
        {
            byte[] original = File.ReadAllBytes(seed);
            // A stable seed per fixture (string hashes differ per process): the same mutations every run.
            var rng = new Random(Path.GetFileName(seed).Aggregate(17, (hash, c) => unchecked(hash * 31 + c)) & 0x7FFFFFFF);
            for (int i = 0; i <= MutationsPerSeed; i++)
            {
                byte[] input = i == 0 ? original : ByteMutator.Mutate(original, rng, []);
                string path = Path.Combine(_folder, "case.zip");
                File.WriteAllBytes(path, input);
                compared++;
                string old = Read(OldReader, path);
                string now = Read(NewReader, path);
                if (old != now)
                    differences.Add($"{Path.GetFileName(seed)} #{i}:\n  old: {old}\n  new: {now}");
            }
        }

        compared.Should().BeGreaterThan(1000);
        string.Join("\n", differences).Should().BeEmpty();
    }

    private static string Read(Func<string, IEnumerable<string>> reader, string path)
    {
        try
        {
            return string.Join(" | ", reader(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return $"refused: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static IEnumerable<string> OldReader(string path)
    {
        using var reader = LegacyZipArchiveReader.Open(path, Us);
        return [.. reader.Entries.Select(e => Describe(e.FullName, e.CollidesAfterDecoding, e.Entry))];
    }

    private static IEnumerable<string> NewReader(string path)
    {
        using var catalog = ZipCatalog.Open(path, Us);
        catalog.Check();
        return [.. catalog.Entries(collisions: true).Select(e => Describe(e.FullName, e.CollidesAfterDecoding, e.Entry))];
    }

    private static string Describe(string name, bool collides, ZipArchiveEntry entry) =>
        $"{name}{(collides ? " (collides)" : "")} {entry.Length}/{entry.CompressedLength} {entry.Crc32:X8} {Content(entry)}";

    private static string Content(ZipArchiveEntry entry)
    {
        try
        {
            using Stream stream = entry.Open();
            return Convert.ToHexString(SHA256.HashData(stream))[..12];
        }
        catch (Exception ex)
        {
            return $"[{ex.GetType().Name}: {ex.Message}]";
        }
    }
}
