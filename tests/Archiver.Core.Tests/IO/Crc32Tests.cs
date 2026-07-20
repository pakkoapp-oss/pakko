using System.Text;
using Archiver.Core.IO;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F128 follow-up: Crc32.Combine is what makes parallel intra-file hashing possible (split a
// large file into independently-hashed chunks, then fold the per-chunk CRC-32 values together
// without re-reading the data). It's a faithful reimplementation of zlib's public-domain
// crc32_combine, so these tests directly prove the reimplementation is correct: combining two
// independently-computed CRCs must equal hashing the concatenation as one continuous stream.
public sealed class Crc32Tests
{
    [Fact]
    public void Combine_TwoHalvesOfKnownString_MatchesWholeStringCrc()
    {
        // "hello world" CRC-32 is a known value used throughout this project's other hash tests.
        uint whole = ComputeCrc32("hello world");
        uint crc1 = ComputeCrc32("hello");
        uint crc2 = ComputeCrc32(" world");

        uint combined = Crc32.Combine(crc1, crc2, " world".Length);

        combined.Should().Be(whole);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(24)]
    public void Combine_ArbitrarySplitPoint_MatchesWholeStringCrc(int splitPoint)
    {
        const string text = "second file content here"; // 25 chars, another known-value fixture elsewhere
        uint whole = ComputeCrc32(text);
        uint crc1 = ComputeCrc32(text[..splitPoint]);
        uint crc2 = ComputeCrc32(text[splitPoint..]);

        uint combined = Crc32.Combine(crc1, crc2, text.Length - splitPoint);

        combined.Should().Be(whole);
    }

    [Fact]
    public void Combine_ThreeChunks_MatchesWholeCrc()
    {
        const string a = "The quick brown fox ";
        const string b = "jumps over the lazy ";
        const string c = "dog.";
        uint whole = ComputeCrc32(a + b + c);

        uint combined = Crc32.Combine(ComputeCrc32(a), ComputeCrc32(b), b.Length);
        combined = Crc32.Combine(combined, ComputeCrc32(c), c.Length);

        combined.Should().Be(whole);
    }

    [Fact]
    public void Combine_LargeRandomBuffer_ChunkedMatchesSequential()
    {
        var rng = new Random(20260720);
        var data = new byte[1_000_003]; // deliberately not a multiple of any "nice" chunk size
        rng.NextBytes(data);

        var seqAcc = new Crc32.Accumulator();
        seqAcc.Update(data);
        uint sequential = seqAcc.Finish();

        // Split into uneven chunks to prove the combine math doesn't assume equal-sized pieces.
        int[] chunkSizes = [123, 999, 500_000, 250_000, data.Length - 123 - 999 - 500_000 - 250_000];
        uint combined = 0;
        int offset = 0;
        bool first = true;
        foreach (int size in chunkSizes)
        {
            var chunkAcc = new Crc32.Accumulator();
            chunkAcc.Update(data.AsSpan(offset, size));
            uint chunkCrc = chunkAcc.Finish();
            combined = first ? chunkCrc : Crc32.Combine(combined, chunkCrc, size);
            first = false;
            offset += size;
        }

        combined.Should().Be(sequential);
    }

    [Fact]
    public void Combine_ZeroLengthSecondChunk_ReturnsFirstCrcUnchanged()
    {
        uint crc1 = ComputeCrc32("anything");

        Crc32.Combine(crc1, 0, 0).Should().Be(crc1);
    }

    private static uint ComputeCrc32(string text)
    {
        var acc = new Crc32.Accumulator();
        acc.Update(Encoding.ASCII.GetBytes(text));
        return acc.Finish();
    }
}
