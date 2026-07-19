using Archiver.Core.IO;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F128: verifies HashDigestAccumulator's carry/overflow arithmetic and display formatting
// directly, independent of file I/O. The two-item CRC-32 case is a real value cross-checked
// against the vendored 7za.exe (`h -scrcCRC32`, T-F114's tool) — 7-Zip's own "h" command runs the
// exact same NanaZip/HashCalc.cpp algorithm this class reproduces, so a match here is real
// external-tool parity proof, not just an internally-consistent unit test.
public sealed class HashDigestAccumulatorTests
{
    [Fact]
    public void ToDisplayString_SingleItem_NoSuffix()
    {
        var acc = new HashDigestAccumulator(4);
        // Little-endian bytes of CRC-32 0x0D4A1185 (a.txt's real CRC-32, see FileHashServiceTests).
        acc.Add(new byte[] { 0x85, 0x11, 0x4A, 0x0D });

        acc.ToDisplayString().Should().Be("0D4A1185");
    }

    [Fact]
    public void ToDisplayString_TwoItems_MatchesRealSevenZipDataSum()
    {
        var acc = new HashDigestAccumulator(4);
        // Little-endian bytes of a.txt's (0x0D4A1185) and b.txt's (0x8D7BD707) real CRC-32
        // values. `7za h -scrcCRC32 a.txt b.txt` reports "CRC32 for data: 9AC5E88C-00000000".
        acc.Add(new byte[] { 0x85, 0x11, 0x4A, 0x0D });
        acc.Add(new byte[] { 0x07, 0xD7, 0x7B, 0x8D });

        acc.ToDisplayString().Should().Be("9AC5E88C-00000000");
    }

    [Fact]
    public void ToDisplayString_ManyAdditions_CarryPropagatesIntoExtraBytes()
    {
        var acc = new HashDigestAccumulator(4);
        // 5 x 0xFFFFFFFF = 21474836475 = 0x4_FFFFFFFB: digest bytes wrap to 0xFFFFFFFB, and the
        // carry (4) lands in the first extra byte. A single Add() can only ever carry at most 1
        // unit past the digest into the extra region (summing two N-byte numbers plus a carry bit
        // is always < 2^(8N+1)), so reaching past the first 4 extra bytes would need on the order
        // of 2^32 additions - unreachable for any real folder, hence not tested here; this proves
        // the cross-loop carry threading itself (digest overflow -> extra[0]) works.
        for (int i = 0; i < 5; i++)
            acc.Add(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        acc.ToDisplayString().Should().Be("FFFFFFFB-00000004");
    }

    [Fact]
    public void ToDisplayString_LargeDigest_LowerCaseNoByteReversal()
    {
        var acc = new HashDigestAccumulator(32);
        var digest = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        acc.Add(digest);

        acc.ToDisplayString().Should().Be("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
    }
}
