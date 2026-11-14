using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F513: an archive over ArchiveLimits.MaxEntries is refused with its own message by every
// operation, before .NET's reader or tar.exe's listing holds its catalog.
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EntryLimitTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // --- Security & Boundary: a ZIP declaring a billion entries in 128 bytes ---

    [Fact]
    public async Task Zip_DeclaringABillionEntries_ListTestAndExtract_SayWhy()
    {
        string zip = Zip64Declaring(1_000_000_000);
        var service = new ZipArchiveService(new GroupPolicyOptions());

        ArchiveListResult listed = await service.ListEntriesAsync(zip);
        ArchiveResult tested = await service.TestAsync([zip]);
        ArchiveResult extracted = await service.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = Path.Combine(_temp.Path, "out"),
        });

        listed.Success.Should().BeFalse();
        listed.ErrorText!.Code.Should().Be(MessageCode.TooManyEntries);
        tested.Errors.Should().ContainSingle().Which.Message.Should().Contain("too many entries");
        extracted.Errors.Should().ContainSingle().Which.Message.Should().Contain("too many entries");
        extracted.CreatedFiles.Should().BeEmpty();
    }

    // The scan never says Clean of an archive it refused to read.
    [Fact]
    public async Task Zip_DeclaringABillionEntries_ScanIsNeverClean()
    {
        string zip = Zip64Declaring(1_000_000_000);
        var scanner = new FakeAmsiScanner();
        var sut = new AntivirusScanService(new TarCapabilities(), groupPolicyOptions: new GroupPolicyOptions(), () => scanner,
            providerState: () => AmsiProviderState.Usable);

        ThreatScanResult result = await sut.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle().Which.Reason.Should().Contain("too many entries");
    }

    // --- Security & Boundary: tar.exe's listing, one line an entry ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TarListing_OneMoreThanTheLimit_IsRefused_AndTarExeEnds(bool verbose)
    {
        string archivePath = Path.Combine(_temp.Path, "three.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c")]);
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);
        scope.EntryLimit = 2;

        Func<Task> list = () => scope.ListAsync(verbose, CancellationToken.None);

        (await list.Should().ThrowAsync<CoreTextIOException>()).Which.Text.Code.Should().Be(MessageCode.TooManyEntries);
    }

    [Fact]
    public async Task TarListing_AtTheLimit_IsRead()
    {
        string archivePath = Path.Combine(_temp.Path, "two.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "a"), ("b.txt", "b")]);
        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);
        scope.EntryLimit = 2;

        (int exit, string stdOut, string stdErr) = await scope.ListAsync(verbose: false, CancellationToken.None);

        exit.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("a.txt").And.Contain("b.txt");
    }

    // --- The real limit, both tar backends: 2,000,001 empty entries (a 1 GB tar, about 20 MB gzipped) ---

    [Fact]
    [Trait("Category", "Slow")]
    public async Task TarGz_OneEntryOverTheRealLimit_IsRefused_ThroughTarExe()
    {
        string archive = TarGzOfEmptyEntries(ArchiveLimits.MaxEntries + 1);

        ArchiveListResult listed = await new TarSandboxedService(new GroupPolicyOptions()).ListEntriesAsync(archive);

        listed.Success.Should().BeFalse();
        listed.ErrorText!.Code.Should().Be(MessageCode.TooManyEntries, listed.ErrorMessage);
    }

    [NativeX64Fact]
    [Trait("Category", "Slow")]
    public async Task TarGz_OneEntryOverTheRealLimit_IsRefused_ThroughTheHelper()
    {
        string archive = TarGzOfEmptyEntries(ArchiveLimits.MaxEntries + 1);
        var gate = new ArchiveintGate(new GroupPolicyOptions(), ArchiveHelperBuild.ExePath);
        (await gate.DecideAsync()).UsesHelper.Should().BeTrue();

        ArchiveListResult listed = await new TarSandboxedService(new GroupPolicyOptions(), gate).ListEntriesAsync(archive);
        ArchiveResult tested = await new TarSandboxedService(new GroupPolicyOptions(), gate).TestAsync([archive]);

        listed.ErrorText!.Code.Should().Be(MessageCode.TooManyEntries, listed.ErrorMessage);
        tested.Errors.Should().ContainSingle().Which.Message.Should().Contain("too many entries");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task TarGz_AtTheRealLimit_IsListed()
    {
        string archive = TarGzOfEmptyEntries(ArchiveLimits.MaxEntries);

        ArchiveListResult listed = await new TarSandboxedService(new GroupPolicyOptions()).ListEntriesAsync(archive);

        listed.Success.Should().BeTrue(listed.ErrorMessage);
        listed.Entries.Should().HaveCount(ArchiveLimits.MaxEntries);
    }

    private string TarGzOfEmptyEntries(int count)
    {
        string path = Path.Combine(_temp.Path, $"e{count}.tar.gz");
        using var gz = new System.IO.Compression.GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest);
        byte[] header = new byte[512];
        for (int i = 0; i < count; i++)
        {
            Array.Clear(header);
            Encoding.ASCII.GetBytes($"d{i / 1000:D5}/f{i:D8}.txt").CopyTo(header, 0);
            Encoding.ASCII.GetBytes("0000644\0" + "0000000\0" + "0000000\0" + "00000000000\0" + "14000000000\0").CopyTo(header, 100);
            header[156] = (byte)'0';
            Encoding.ASCII.GetBytes("ustar\0" + "00").CopyTo(header, 257);
            "        "u8.CopyTo(header.AsSpan(148));
            int sum = 0;
            foreach (byte b in header)
                sum += b;
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
            gz.Write(header);
        }
        gz.Write(new byte[1024]);
        return path;
    }

    // A local header's signature, then an empty central directory whose Zip64 end record declares `entries`; the classic record
    // holds the 0xFFFF marker, so .NET would expect the Zip64 count.
    private string Zip64Declaring(ulong entries)
    {
        string path = Path.Combine(_temp.Path, "billion.zip");
        using var w = new BinaryWriter(File.Create(path), Encoding.ASCII);
        w.Write(0x04034b50u); w.Write(new byte[26]);
        w.Write(0x06064b50u); w.Write(44UL); w.Write((ushort)45); w.Write((ushort)45);
        w.Write(0u); w.Write(0u); w.Write(entries); w.Write(entries); w.Write(0UL); w.Write(0UL);
        w.Write(0x07064b50u); w.Write(0u); w.Write(30UL); w.Write(1u);
        w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write(ushort.MaxValue); w.Write(ushort.MaxValue);
        w.Write(uint.MaxValue); w.Write(uint.MaxValue); w.Write((ushort)0);
        return path;
    }
}
