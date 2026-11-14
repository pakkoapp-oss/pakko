using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip;
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

    [Fact]
    public void ZipReader_HoldsAtMostItsBudgetPerEntry()
    {
        string zip = ZipOfEmptyEntries();
        var codePages = ZipNameCodePages.FromCodePages(437, 1252);

        long perEntry = LivePerEntry(() => ZipArchiveReader.Open(zip, codePages), reader => reader.Dispose());

        perEntry.Should().BeLessThanOrEqualTo(740, "measured 592 B (.NET's ZipArchive about 455 of them)");
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
