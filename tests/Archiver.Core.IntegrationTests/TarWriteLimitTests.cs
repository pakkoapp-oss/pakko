using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F561: a gzip holding a ZIP written as a stream (sizes only after the data) reaches tar.exe,
/// whose "-tv" prints such an entry as 0 bytes, so the compression-ratio check (T-F94) sees nothing.
/// A tar.exe run may write no more than the entries it extracts declare; past that it is stopped.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarWriteLimitTests : IDisposable
{
    private const int BombMiB = 64;

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    // ZipArchive over a stream it cannot seek writes each entry's sizes in a data descriptor.
    private string StreamedZipInGzip() => StreamedZipInGzip(_temp.Path);

    internal static string StreamedZipInGzip(string folder)
    {
        string path = Path.Combine(folder, "streamed.zip.gz");
        using (FileStream file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        using (var zip = new ZipArchive(new ForwardOnly(gzip), ZipArchiveMode.Create))
        using (Stream entry = zip.CreateEntry("big.bin", CompressionLevel.Optimal).Open())
        {
            byte[] zeros = new byte[1 << 20];
            for (int i = 0; i < BombMiB; i++)
                entry.Write(zeros);
        }
        return path;
    }

    [Integration]
    public async Task ExtractAsync_EntryLargerThanTarExePrints_StopsAndWritesNothing()
    {
        string archive = StreamedZipInGzip();
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        });

        CoreText error = result.Errors.Should().ContainSingle().Which.Text!;
        error.Code.Should().Be(MessageCode.CannotExtractArchive);
        error.Arguments.OfType<CoreText>().Should().ContainSingle().Which.Code.Should().Be(MessageCode.ContentLargerThanDeclared);
        result.CreatedFiles.Should().BeEmpty();
        (Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    [Integration]
    public async Task TestAsync_EntryLargerThanTarExePrints_Fails()
    {
        string archive = StreamedZipInGzip();

        ArchiveResult result = await _sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.ContentLargerThanDeclared);
    }

    [Integration]
    public async Task ScanAsync_EntryLargerThanTarExePrints_IsNeverClean()
    {
        string archive = StreamedZipInGzip();
        var service = new AntivirusScanService(new TarCapabilities(), groupPolicyOptions: new GroupPolicyOptions(),
            () => new FakeAmsiScanner(), providerState: () => AmsiProviderState.Usable);

        ThreatScanResult result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archive] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().Contain(f => f.ReasonText != null && f.ReasonText.Code == MessageCode.ContentLargerThanDeclared);
    }

    // The first-copy pass ("-q") writes each name's first copy: its limit is the first copy's
    // size, not the last one's.
    [Integration]
    public async Task ExtractAsync_DuplicateNameWithTheLargerFirstCopy_ExtractsBoth()
    {
        string archive = Path.Combine(_temp.Path, "dup.tar");
        byte[] first = new byte[3 << 20];
        new Random(561).NextBytes(first);
        TarBuilder.WriteTar(archive,
        [
            new TarBuilder.Entry { Name = "dup.bin", Content = first },
            new TarBuilder.Entry { Name = "dup.bin", Content = Encoding.ASCII.GetBytes("second") },
        ]);
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename,
        });

        result.Errors.Should().BeEmpty();
        File.ReadAllBytes(Path.Combine(dest, "dup.bin")).Should().Equal(first);
        File.ReadAllText(Path.Combine(dest, "dup (1).bin")).Should().Be("second");
    }
}
