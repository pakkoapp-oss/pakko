using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F513: an archive with more entries than ArchiveLimits.MaxEntries is refused with its own message
// before its catalog is held: by the count a ZIP declares, by the records its directory really holds,
// and by the entries the archive helper reports.
public sealed class ArchiveLimitsTests : IDisposable
{
    private static readonly ZipNameCodePages Us = ZipNameCodePages.FromCodePages(437, 1252);
    private readonly string _folder = Directory.CreateTempSubdirectory("pakko-limits-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // --- Happy path ---

    [Fact]
    public void ZipNames_AtTheLimit_AreRead()
    {
        using MemoryStream zip = ZipOf(3);

        RawZipEntryLocator.ReadEntryNames(zip, Us, maxEntries: 3).Select(n => n.Name).Should().Equal("f0.txt", "f1.txt", "f2.txt");
    }

    [Fact]
    public void HelperListing_AtTheLimit_IsTaken()
    {
        var run = new ArchiveHelperOutput(ArchiveHelperMode.List, entryLimit: 2);

        Feed(run, "V 6C696261726368697665", Entry(0), Entry(1), "D");

        run.Entries.Should().HaveCount(2);
    }

    // --- Security & Boundary ---

    [Fact]
    public void ZipNames_DeclaringOneMoreThanTheLimit_AreRefused()
    {
        using MemoryStream zip = ZipOf(3);

        Refused(() => RawZipEntryLocator.ReadEntryNames(zip, Us, maxEntries: 2), 2);
    }

    // A directory holding more records than its end record declares: .NET's reader throws on the
    // record past the declared count, and since T-F529 Pakko's own walk does too - before the limit,
    // which the declared count is already within.
    [Fact]
    public void ZipNames_MoreRecordsThanDeclared_AreRefusedAtTheDeclaredCount()
    {
        using MemoryStream zip = ZipOf(3);
        DeclareEntries(zip, 1);

        FluentActions.Invoking(() => RawZipEntryLocator.ReadEntryNames(zip, Us, maxEntries: 2))
            .Should().Throw<InvalidDataException>().WithMessage(RawZipEntryLocator.NumEntriesWrongText);
    }

    [Fact]
    public void Zip64_DeclaringABillionEntries_IsRefusedAtTheRealLimit()
    {
        using MemoryStream zip = Zip64Declaring(1_000_000_000);

        Refused(() => RawZipEntryLocator.ReadEntryNames(zip, Us), ArchiveLimits.MaxEntries);
        Refused(() => RawZipEntryLocator.LocateAll(zip), ArchiveLimits.MaxEntries);
        Refused(() => RawZipEntryLocator.HasAnyEncryptedEntry(zip), ArchiveLimits.MaxEntries);
        Refused(() => RawZipEntryLocator.FindLocalHeaderMismatches(zip), ArchiveLimits.MaxEntries);
    }

    [Fact]
    public void HelperListing_OneMoreThanTheLimit_IsRefused_NotABrokenProtocol()
    {
        var run = new ArchiveHelperOutput(ArchiveHelperMode.Test, entryLimit: 2);

        Refused(() => Feed(run, "V 6C696261726368697665", Entry(0), Entry(1), Entry(2)), 2);
    }

    // --- T-F529: the names' characters together ---

    [Fact]
    public void ZipNames_AtTheNameLimit_AreRead_ByEveryWalk()
    {
        string zip = ZipFileOf(3); // f0.txt, f1.txt, f2.txt: 18 characters
        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, ZipCatalog.DefaultBatchEntries, false, maxNameChars: 18);

        catalog.Names().Should().HaveCount(3);
        catalog.Entries().Should().HaveCount(3);
        catalog.Located().Should().HaveCount(3);
    }

    [Fact]
    public void ZipNames_OneCharacterOverTheNameLimit_AreRefused_ByEveryWalk()
    {
        string zip = ZipFileOf(3);
        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, ZipCatalog.DefaultBatchEntries, false, maxNameChars: 17);

        RefusedNames(() => _ = catalog.Names().ToList(), 17);
        RefusedNames(() => catalog.Check(), 17);
        RefusedNames(() => _ = catalog.Located().ToList(), 17);
    }

    // A one-byte raw name carrying a long Unicode Path extra (0x7075): what is held is the decoded name.
    [Fact]
    public void ZipName_LongOnlyInItsUnicodePathExtra_IsCountedAsDecoded()
    {
        byte[] raw = "a"u8.ToArray();
        byte[] utf8 = Encoding.UTF8.GetBytes(new string('n', 100));
        string zip = LegacyZipBuilder.Write(Path.Combine(_folder, "extra.zip"),
            new LegacyZipBuilder.Entry(raw, [], CentralExtra: LegacyZipBuilder.UnicodePathExtra(raw, utf8)));
        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, ZipCatalog.DefaultBatchEntries, false, maxNameChars: 99);

        RefusedNames(() => _ = catalog.Names().ToList(), 99);
    }

    [Fact]
    public void HelperListing_NamesAndLinksOverTheNameLimit_AreRefused_NotABrokenProtocol()
    {
        var atLimit = new ArchiveHelperOutput(ArchiveHelperMode.List, nameCharLimit: 3);
        Feed(atLimit, "V 6C696261726368697665", "E 0 40960 0 1700000000 0 0 0 0061 00620063", "D");
        atLimit.Entries.Should().HaveCount(1, "the name 'a' and the link 'bc': 3 characters");

        var over = new ArchiveHelperOutput(ArchiveHelperMode.List, nameCharLimit: 2);
        CoreTextIOException refused = FluentActions.Invoking(() =>
                Feed(over, "V 6C696261726368697665", "E 0 40960 0 1700000000 0 0 0 0061 00620063"))
            .Should().Throw<CoreTextIOException>().Which;
        refused.Text.Code.Should().Be(MessageCode.CatalogTooLarge);
    }

    [Fact]
    public void TheNameMessage_NamesTheLimit()
    {
        ArchiveLimits.CatalogTooLarge(ArchiveLimits.MaxCatalogNameChars).Text.English.Should().Contain("too long").And.Contain("134");
    }

    [Fact]
    public void TheMessage_NamesTheLimit()
    {
        ArchiveLimits.TooManyEntries(2_000_000).Text.English.Should().Contain("too many entries").And.Contain("2");
    }

    // --- helpers ---

    private static void Refused(Action read, int limit)
    {
        CoreTextIOException refused = FluentActions.Invoking(read).Should().Throw<CoreTextIOException>().Which;
        refused.Text.Code.Should().Be(MessageCode.TooManyEntries);
        refused.Text.English.Should().Be(ArchiveLimits.TooManyEntries(limit).Text.English);
    }

    private static void RefusedNames(Action read, int limit)
    {
        CoreTextIOException refused = FluentActions.Invoking(read).Should().Throw<CoreTextIOException>().Which;
        refused.Text.Code.Should().Be(MessageCode.CatalogTooLarge);
        refused.Text.English.Should().Be(ArchiveLimits.CatalogTooLarge(limit).Text.English);
    }

    private string ZipFileOf(int entries)
    {
        string path = Path.Combine(_folder, $"{entries}.zip");
        using MemoryStream zip = ZipOf(entries);
        File.WriteAllBytes(path, zip.ToArray());
        return path;
    }

    private static string Entry(int index) => $"E {index} 32768 5 1700000000 0 0 0 0061 -";

    private static void Feed(ArchiveHelperOutput run, params string[] lines)
    {
        foreach (string line in lines)
            run.Feed(Encoding.ASCII.GetBytes(line));
    }

    private static MemoryStream ZipOf(int entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < entries; i++)
                zip.CreateEntry($"f{i}.txt", CompressionLevel.NoCompression);
        }
        stream.Position = 0;
        return stream;
    }

    // The classic end record's two counts (this disk, total) at offsets 8 and 10.
    private static void DeclareEntries(MemoryStream zip, ushort count)
    {
        byte[] bytes = zip.GetBuffer();
        int eocd = (int)zip.Length - 22;
        BitConverter.GetBytes(0x06054b50u).Should().Equal(bytes[eocd..(eocd + 4)]);
        BitConverter.GetBytes(count).CopyTo(bytes, eocd + 8);
        BitConverter.GetBytes(count).CopyTo(bytes, eocd + 10);
    }

    // A local header's signature, then an empty central directory whose Zip64 end record declares `entries`; the classic record
    // holds the 0xFFFF marker, so .NET would expect the Zip64 count.
    private static MemoryStream Zip64Declaring(ulong entries)
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(0x04034b50u); w.Write(new byte[26]);
            w.Write(0x06064b50u); w.Write(44UL); w.Write((ushort)45); w.Write((ushort)45);
            w.Write(0u); w.Write(0u); w.Write(entries); w.Write(entries); w.Write(0UL); w.Write(0UL);
            w.Write(0x07064b50u); w.Write(0u); w.Write(30UL); w.Write(1u);
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write(ushort.MaxValue); w.Write(ushort.MaxValue);
            w.Write(uint.MaxValue); w.Write(uint.MaxValue); w.Write((ushort)0);
        }
        stream.Position = 0;
        return stream;
    }
}
