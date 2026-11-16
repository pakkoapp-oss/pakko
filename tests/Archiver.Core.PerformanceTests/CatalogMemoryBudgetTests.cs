using System.IO.Compression;
using System.Text;
using Archiver.Core.IntegrationTests;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Services.Zip;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace Archiver.Core.PerformanceTests;

[CollectionDefinition("CatalogMemory", DisableParallelization = true)]
public sealed class CatalogMemoryGroup;

// T-F513 step 2: the memory an archive's catalog holds per entry, as live bytes after a full
// collection (a deterministic counter, unlike a process's peak), with the result or the reader
// held. The budgets are 2026-11-14's measurements plus about a quarter; T-F529 (reading in batches)
// lowers them. Alone in its collection: another test allocating meanwhile would count here.
[Collection("CatalogMemory")]
[Trait("Category", "Slow")]
public sealed class CatalogMemoryBudgetTests(ITestOutputHelper output) : IDisposable
{
    private const int Entries = 200_000;
    private readonly string _folder = Directory.CreateTempSubdirectory("pakko-catalog-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // T-F529: what a test or an extraction holds while it walks the archive, measured from the
    // entry filter halfway through (the filter runs inside the walk). Before T-F529 every entry was
    // held from the open on: 592 B an entry, .NET's ZipArchive about 455 of them.
    [Fact]
    public async Task ZipTest_WhileItWalks_HoldsAtMostItsBudgetPerEntry()
    {
        string zip = ZipOfEmptyEntries();
        var service = new ZipArchiveService(new GroupPolicyOptions());
        var probe = new HalfwayProbe(Entries);

        ArchiveResult result = await service.TestAsync([zip], entryFilter: probe.Filter);

        result.Errors.Should().BeEmpty();
        Report(probe.Before, probe.Halfway).Should().BeLessThanOrEqualTo(120, "measured 93 B: the name set T-F234's collision check keeps, and one batch");
    }

    [Fact]
    public async Task ZipExtraction_WhileItWalks_HoldsAtMostItsBudgetPerEntry()
    {
        string zip = ZipOfEmptyEntries();
        var service = new ZipArchiveService(new GroupPolicyOptions());
        var probe = new HalfwayProbe(Entries, pass: false);

        await service.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = Path.Combine(_folder, "out"),
            EntryFilter = probe.Filter,
        });

        probe.Halfway.Should().BeGreaterThan(0, "the filter saw the walk's middle");
        Report(probe.Before, probe.Halfway).Should().BeLessThanOrEqualTo(30, "measured 15 B in the first walk: one batch, no entry list");
    }

    // T-F529 review: the collision check keeps each name's first raw bytes, which a 0x7075 extra
    // can make 64 KiB behind a short decoded name the name limit counts. Held raw, 600 such names
    // were 36 MB live halfway through the walk.
    [Fact]
    public void ZipCollisionCheck_RawNamesBehindShortOnes_AreNotHeld()
    {
        const int count = 600;
        var entries = new LegacyZipBuilder.Entry[count];
        for (int i = 0; i < count; i++)
        {
            byte[] raw = Encoding.ASCII.GetBytes($"{i:D8}".PadRight(60_000, 'r'));
            entries[i] = new LegacyZipBuilder.Entry(raw, [], CentralExtra: LegacyZipBuilder.UnicodePathExtra(raw, Encoding.UTF8.GetBytes($"f{i}.txt")));
        }
        string zip = LegacyZipBuilder.Write(Path.Combine(_folder, "raw.zip"), entries);
        using var catalog = ZipCatalog.Open(zip, ZipNameCodePages.FromCodePages(437, 1252));
        long before = GC.GetTotalMemory(forceFullCollection: true);
        long halfway = 0;
        int seen = 0;

        foreach (NamedZipEntry entry in catalog.Entries(collisions: true))
        {
            entry.CollidesAfterDecoding.Should().BeFalse();
            if (++seen == count - 1)
                halfway = GC.GetTotalMemory(forceFullCollection: true);
        }

        long held = halfway - before;
        output.WriteLine($"{count} raw names of 60,000 bytes: {held >> 10} KiB live near the walk's end");
        held.Should().BeLessThan(6 << 20, "one batch (1 MiB of records) and a digest a name, not 36 MB of raw names");
    }

    // T-F541: what a tar extraction holds while its filter decides, halfway through the names: the
    // names and a size each. Before, a dictionary of sizes, a list of the files and two copies of the
    // names for the root decision were held beside them.
    [Fact]
    public async Task TarExtraction_WhileItSelects_HoldsAtMostItsBudgetPerEntry()
    {
        string tar = TarGzOfEmptyEntries();
        var service = new TarSandboxedService(new GroupPolicyOptions());
        var probe = new HalfwayProbe(Entries, pass: false);

        await service.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tar],
            DestinationFolder = Path.Combine(_folder, "out"),
            EntryFilter = probe.Filter,
        });

        probe.Halfway.Should().BeGreaterThan(0, "the filter saw the names' middle");
        Report(probe.Before, probe.Halfway).Should().BeLessThanOrEqualTo(100, "the names (about 64 B each), their array and their sizes");
    }

    // T-F542: the archive helper's extraction, measured as tar.exe's: one compact entry each, not the
    // helper's own listing beside it and a copy at every step.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task HelperExtraction_WhileItSelects_HoldsAtMostItsBudgetPerEntry()
    {
        string tar = TarGzOfEmptyEntries();
        var service = new TarSandboxedService(new GroupPolicyOptions()) { Helper = HelperSetupFor(tar) };
        var probe = new HalfwayProbe(Entries, pass: false);

        await service.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tar],
            DestinationFolder = Path.Combine(_folder, "out"),
            EntryFilter = probe.Filter,
        });

        probe.Halfway.Should().BeGreaterThan(0, "the filter saw the names' middle");
        Report(probe.Before, probe.Halfway).Should().BeLessThanOrEqualTo(140, "a name (about 64 B) and its entry");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static HelperSetup HelperSetupFor(string archive)
    {
        using TarSandboxScope scope = TarSandboxScope.CreateAsync(archive, false, CancellationToken.None, helperSource: ArchiveHelperBuild.ExePath)
            .GetAwaiter().GetResult();
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Probe);
        scope.RunHelperAsync(ArchiveHelperMode.Probe, [], null, output, CancellationToken.None).GetAwaiter().GetResult();
        return new HelperSetup(ArchiveHelperBuild.ExePath, output.Capabilities);
    }

    // The live bytes before the operation starts and at the filter's call in the middle of the walk.
    private sealed class HalfwayProbe(int entries, bool pass = true)
    {
        private int _calls;
        public long Before { get; } = GC.GetTotalMemory(forceFullCollection: true);
        public long Halfway { get; private set; }

        public bool Filter(string name, bool isFolder)
        {
            int call = ++_calls;
            if (call == entries / 2)
                Halfway = GC.GetTotalMemory(forceFullCollection: true);
            return pass;
        }
    }

    [Fact]
    public async Task ZipListing_HoldsAtMostItsBudgetPerEntry()
    {
        string zip = ZipOfEmptyEntries();
        var service = new ZipArchiveService(new GroupPolicyOptions());

        long perEntry = await LivePerEntryAsync(() => service.ListEntriesAsync(zip));

        perEntry.Should().BeLessThanOrEqualTo(210, "measured 168 B");
    }

    [Fact]
    public async Task TarListing_HoldsAtMostItsBudgetPerEntry()
    {
        string tar = TarGzOfEmptyEntries();
        var service = new TarSandboxedService(new GroupPolicyOptions());

        long perEntry = await LivePerEntryAsync(() => service.ListEntriesAsync(tar));

        perEntry.Should().BeLessThanOrEqualTo(260, "measured 210 B");
    }

    // T-F537: a caller with no synchronization context (pakko's console) resumes on the thread that
    // finished the listing, on top of its frames. Measured 540 B an entry there before the fix, the
    // frames below still holding tar.exe's output and both listings while the caller used the result.
    [Fact]
    public async Task TarListing_ToACallerWithoutAContext_HoldsAtMostItsBudgetPerEntry()
    {
        string tar = TarGzOfEmptyEntries();
        var service = new TarSandboxedService(new GroupPolicyOptions());

        long perEntry = await Task.Run(async () =>
        {
            long before = GC.GetTotalMemory(forceFullCollection: true);
            ArchiveListResult held = await service.ListEntriesAsync(tar).ConfigureAwait(false);
            long after = GC.GetTotalMemory(forceFullCollection: true);
            held.Entries.Should().HaveCount(Entries, held.ErrorMessage);
            GC.KeepAlive(held);
            return Report(before, after);
        });

        perEntry.Should().BeLessThanOrEqualTo(260, "the same as a caller with a context: 210 B");
    }

    // T-F529: what a tar list allocates on the way, its passing copies (tar.exe's output as text,
    // its lines) - a deterministic stand-in for the peak, which the process measures only once.
    [Fact]
    public async Task TarListing_AllocatesAtMostItsBudgetPerEntry()
    {
        string tar = TarGzOfEmptyEntries();
        var service = new TarSandboxedService(new GroupPolicyOptions());
        (await service.ListEntriesAsync(tar)).Success.Should().BeTrue("a first list pays the one-time costs");

        long before = GC.GetTotalAllocatedBytes(precise: true);
        ArchiveListResult listed = await service.ListEntriesAsync(tar);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        listed.Entries.Should().HaveCount(Entries, listed.ErrorMessage);
        long perEntry = allocated / Entries;
        output.WriteLine($"{Entries} entries: {allocated >> 20} MiB allocated, {perEntry} B an entry");
        perEntry.Should().BeLessThanOrEqualTo(2_400, "measured 1,950 B; 3,202 with -tv read as one text and its lines");
    }

    // T-F539: what a tar.exe scan keeps of "-t" once it has its names - the names, not the bytes
    // tar.exe printed beside them (held for T-F465 until a selection needs them). 2,000 names of
    // 30,000 characters: about 2 bytes a character are the names themselves.
    [Fact]
    public async Task TarScan_LongNames_KeepsTheNamesOnly()
    {
        const int count = 2_000, length = 30_000;
        string tar = TarGzOfLongNames(count, length);
        long before = GC.GetTotalMemory(forceFullCollection: true);
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(tar, needsOutputDir: true, CancellationToken.None);
        object scanned = await TarExeReader.ScanForUnsafeEntriesAsync(scope, CancellationToken.None);
        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(scanned);

        double perChar = (after - before) / (double)(count * (long)length);
        output.WriteLine($"{count} names of {length} characters: {(after - before) >> 20} MiB live, {perChar:F2} B a character");
        perChar.Should().BeLessThanOrEqualTo(2.4, "the names are 2 B a character; tar.exe's printed bytes were 1 more");
    }

    private string TarGzOfLongNames(int count, int length)
    {
        string path = Path.Combine(_folder, "long.tar.gz");
        using var gz = new GZipStream(File.Create(path), CompressionLevel.Fastest);
        for (int i = 0; i < count; i++)
        {
            byte[] name = Encoding.ASCII.GetBytes($"{i:D6}".PadRight(length, 'n'));
            gz.Write(TarHeader("././@LongLink", name.Length + 1, (byte)'L'));
            byte[] data = new byte[(name.Length + 1 + 511) / 512 * 512];
            name.CopyTo(data, 0);
            gz.Write(data);
            gz.Write(TarHeader(Encoding.ASCII.GetString(name, 0, 99), 0, (byte)'0'));
        }
        gz.Write(new byte[1024]);
        return path;
    }

    // A GNU header (magic "ustar  "), as GNU tar writes one for a long name's 'L' record.
    private static byte[] TarHeader(string name, long size, byte type)
    {
        byte[] header = new byte[512];
        Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
        Encoding.ASCII.GetBytes("0000644\0" + "0000000\0" + "0000000\0" + Convert.ToString(size, 8).PadLeft(11, '0') + "\0" + "14000000000\0").CopyTo(header, 100);
        header[156] = type;
        Encoding.ASCII.GetBytes("ustar  \0").CopyTo(header, 257);
        "        "u8.CopyTo(header.AsSpan(148));
        int sum = 0;
        foreach (byte b in header)
            sum += b;
        Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
        return header;
    }

    private long LivePerEntry<T>(Func<T> hold, Action<T> release)
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        T held = hold();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        release(held);
        GC.KeepAlive(held);
        return Report(before, after);
    }

    private async Task<long> LivePerEntryAsync(Func<Task<ArchiveListResult>> list)
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        ArchiveListResult held = await list();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        held.Entries.Should().HaveCount(Entries, held.ErrorMessage);
        GC.KeepAlive(held);
        return Report(before, after);
    }

    private long Report(long before, long after)
    {
        long perEntry = (after - before) / Entries;
        output.WriteLine($"{Entries} entries: {(after - before) >> 20} MiB live, {perEntry} B an entry");
        return perEntry;
    }

    private static string Name(int i) => $"d{i / 1000:D5}/f{i:D8}.txt";

    private string ZipOfEmptyEntries()
    {
        string path = Path.Combine(_folder, "entries.zip");
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            for (int i = 0; i < Entries; i++)
                zip.CreateEntry(Name(i), CompressionLevel.NoCompression);
        }
        return path;
    }

    private string TarGzOfEmptyEntries()
    {
        string path = Path.Combine(_folder, "entries.tar.gz");
        using var gz = new GZipStream(File.Create(path), CompressionLevel.Fastest);
        byte[] header = new byte[512];
        for (int i = 0; i < Entries; i++)
        {
            Array.Clear(header);
            Encoding.ASCII.GetBytes(Name(i)).CopyTo(header, 0);
            Encoding.ASCII.GetBytes("0000644 0000000 0000000 00000000000 14000000000 ").CopyTo(header, 100);
            foreach (int end in (int[])[107, 115, 123, 135, 147])
                header[end] = 0;
            header[156] = (byte)'0';
            Encoding.ASCII.GetBytes("ustar 00").CopyTo(header, 257);
            header[262] = 0;
            "        "u8.CopyTo(header.AsSpan(148));
            int sum = 0;
            foreach (byte b in header)
                sum += b;
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0')).CopyTo(header, 148);
            header[154] = 0;
            gz.Write(header);
        }
        gz.Write(new byte[1024]);
        return path;
    }
}
