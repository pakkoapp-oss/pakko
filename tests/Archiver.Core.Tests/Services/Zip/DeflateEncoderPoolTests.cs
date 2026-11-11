using System.IO.Compression;
using Archiver.Core.Services.Zip;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

// T-F318: one DeflateEncoder per parallel worker, reset between entries, instead of a new
// DeflateStream (and a new zlib-ng state) per entry - dotnet/runtime#134700.
public sealed class DeflateEncoderPoolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public DeflateEncoderPoolTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static byte[] Text(int length, int seed)
    {
        const string words = "the quick brown fox jumps over a lazy dog ";
        var rng = new Random(seed);
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)words[rng.Next(words.Length)];
        return data;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> deflated)
    {
        using var input = new MemoryStream(deflated.ToArray());
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void Rent_AfterReturn_GivesTheSameDeflaterBack()
    {
        using var pool = new DeflateEncoderPool(CompressionLevel.Optimal);
        DeflateEncoderPool.Deflater first = pool.Rent();
        pool.Return(first);

        DeflateEncoderPool.Deflater second = pool.Rent();
        pool.Return(second);

        second.Should().BeSameAs(first);
        pool.Created.Should().Be(1);
    }

    [Fact]
    public void Rent_TwiceWithoutReturn_GivesTwoDeflaters()
    {
        using var pool = new DeflateEncoderPool(CompressionLevel.Optimal);
        DeflateEncoderPool.Deflater first = pool.Rent();
        DeflateEncoderPool.Deflater second = pool.Rent();
        pool.Return(first);
        pool.Return(second);

        second.Should().NotBeSameAs(first);
        pool.Created.Should().Be(2);
    }

    [Fact]
    public void Dispose_DisposesTheDeflatersItHolds()
    {
        var pool = new DeflateEncoderPool(CompressionLevel.Optimal);
        DeflateEncoderPool.Deflater deflater = pool.Rent();
        pool.Return(deflater);

        pool.Dispose();

        Action use = () => deflater.Compress(Text(1_000, 6));
        use.Should().Throw<ObjectDisposedException>();
    }

    // A source that fails mid-read leaves the deflater usable: every compression resets first.
    [Fact]
    public void Compress_AfterASourceThatFailedMidRead_TheDeflaterStillGivesWhatAFreshOneGives()
    {
        byte[] next = Text(30_000, 7);
        using var fresh = new DeflateEncoderPool.Deflater(6);
        byte[] expected = fresh.Compress(next).ToArray();
        using var deflater = new DeflateEncoderPool.Deflater(6);

        Action failing = () => ZipEntryCompressor.Compress(new FailingStream(), CompressionLevel.Optimal, deflater: deflater);
        failing.Should().Throw<IOException>();
        CompressedEntryData after = ZipEntryCompressor.Compress(new MemoryStream(next), CompressionLevel.Optimal, deflater: deflater);

        after.CompressedBytes.Should().Equal(expected);
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (++_reads > 2) throw new IOException("The device is not ready.");
            buffer.Fill((byte)'a');
            return buffer.Length;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Compress_MatchesDeflateStreamAtTheSameLevelAndRoundTrips(CompressionLevel level)
    {
        byte[] data = Text(200_000, 1);
        using var reference = new MemoryStream();
        using (var deflate = new DeflateStream(reference, level, leaveOpen: true))
            deflate.Write(data);

        using var deflater = new DeflateEncoderPool.Deflater(DeflateEncoderPool.QualityOf(level));
        byte[] packed = deflater.Compress(data).ToArray();

        // Not byte for byte: the encoder ends its stream a byte shorter at some levels (measured,
        // .NET 11). The level is the same when the size is.
        packed.Length.Should().BeCloseTo((int)reference.Length, 8);
        Inflate(packed).Should().Equal(data);
    }

    [Fact]
    public void Compress_ReusedForADifferentInput_GivesWhatAFreshDeflaterGives()
    {
        byte[] first = new byte[50_000];
        new Random(2).NextBytes(first);
        byte[] second = Text(120_000, 3);
        using var fresh = new DeflateEncoderPool.Deflater(6);
        byte[] expected = fresh.Compress(second).ToArray();

        using var reused = new DeflateEncoderPool.Deflater(6);
        _ = reused.Compress(first);
        byte[] actual = reused.Compress(second).ToArray();

        actual.Should().Equal(expected);
        Inflate(actual).Should().Equal(second);
    }

    [Fact]
    public void Compress_ThroughAPoolDeflater_EmptyAndIncompressibleEntriesStayStored()
    {
        byte[] random = new byte[64_000];
        new Random(4).NextBytes(random);
        using var pool = new DeflateEncoderPool(CompressionLevel.Fastest);
        DeflateEncoderPool.Deflater deflater = pool.Rent();

        CompressedEntryData empty = ZipEntryCompressor.Compress(new MemoryStream(), CompressionLevel.Fastest, deflater: deflater);
        CompressedEntryData grown = ZipEntryCompressor.Compress(new MemoryStream(random), CompressionLevel.Fastest, deflater: deflater);
        CompressedEntryData text = ZipEntryCompressor.Compress(new MemoryStream(Text(64_000, 5)), CompressionLevel.Fastest, deflater: deflater);
        pool.Return(deflater);

        empty.Method.Should().Be(0);
        empty.CompressedBytes.Should().BeEmpty();
        grown.Method.Should().Be(0);
        grown.CompressedBytes.Should().Equal(random);
        text.Method.Should().Be(8);
        Inflate(text.CompressedBytes).Should().Equal(Text(64_000, 5));
    }

    // The run's compute gate bounds the deflaters: many entries through a window of four never
    // build more than four, and the archive reads back.
    [Fact]
    public async Task RunPipelineAsync_ThreeHundredSmallFiles_BuildsNoMoreDeflatersThanTheWindow()
    {
        const int windowCapacity = 4;
        var items = new List<FileWorkItem>();
        for (int i = 0; i < 300; i++)
        {
            string path = Path.Combine(_tempDir, $"f{i}.txt");
            byte[] data = Text(2_000 + i, i);
            File.WriteAllBytes(path, data);
            items.Add(new FileWorkItem(path, $"f{i}.txt", FileWorkKind.File, data.Length, DateTime.Now));
        }
        var settings = new ParallelSingleArchiveWriter.CompressionSettings(CompressionLevel.Optimal);
        string archivePath = Path.Combine(_tempDir, "out.zip");
        using var pool = new DeflateEncoderPool(settings.Level);

        await ParallelSingleArchiveWriter.RunPipelineAsync(
            archivePath, items,
            (item, ct) => ParallelSingleArchiveWriter.CompressEligibleFileAsync(item, settings, pool, null, ct),
            (_, _) => throw new InvalidOperationException("no file here is large"),
            windowCapacity, totalBytes: 0, progress: null, reportError: e => throw new InvalidOperationException(e.Message),
            CancellationToken.None);

        pool.Created.Should().BeInRange(1, windowCapacity);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Should().HaveCount(300);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using var content = new MemoryStream();
            using (Stream s = entry.Open()) s.CopyTo(content);
            content.ToArray().Should().Equal(File.ReadAllBytes(Path.Combine(_tempDir, entry.Name)));
        }
    }
}
