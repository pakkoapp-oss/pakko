namespace Archiver.Core.IO;

/// <summary>
/// T-F128: reproduces NanaZip's folder-hash combine algorithm bit-for-bit — verified against
/// NanaZip.UI.Modern/SevenZip/CPP/7zip/UI/Common/HashCalc.cpp's <c>AddDigests</c> and
/// <c>CHasherState::WriteToString</c> — so Pakko's folder DataSum/NamesSum values match
/// NanaZip's for the same folder. A little-endian multi-precision addition with carry, not a
/// concatenation: each item's digest is added into a running <c>digestSize + 8</c>-byte
/// accumulator (the extra 8 bytes absorb carry overflow — real for CRC-32's 4-byte digest after
/// only a couple of files).
/// </summary>
internal sealed class HashDigestAccumulator
{
    private const int ExtraSize = 8;
    private readonly byte[] _bytes;
    private readonly int _digestSize;
    private int _itemCount;

    public HashDigestAccumulator(int digestSize)
    {
        _digestSize = digestSize;
        _bytes = new byte[digestSize + ExtraSize];
    }

    public void Add(ReadOnlySpan<byte> itemDigest)
    {
        _itemCount++;
        int carry = 0;
        for (int i = 0; i < _digestSize; i++)
        {
            int sum = _bytes[i] + itemDigest[i] + carry;
            _bytes[i] = (byte)sum;
            carry = sum >> 8;
        }
        for (int i = _digestSize; i < _digestSize + ExtraSize; i++)
        {
            int sum = _bytes[i] + carry;
            _bytes[i] = (byte)sum;
            carry = sum >> 8;
        }
    }

    /// <summary>
    /// Formats the accumulated digest, appending a 4- or 8-byte overflow suffix (e.g.
    /// "3B4FE1AC-00000001") whenever more than one item was combined — mirrors
    /// <c>CHasherState::WriteToString</c> exactly, including showing the suffix even when its
    /// bytes happen to be all zero.
    /// </summary>
    public string ToDisplayString()
    {
        string main = HashHexToString(_bytes.AsSpan(0, _digestSize));
        if (_itemCount == 1)
            return main;

        // A single Add() call can only ever carry at most 1 unit past the digest into this 8-byte
        // region (summing two N-byte numbers plus a carry bit is always < 2^(8N+1)), so the
        // ">4 -> 8-byte suffix" branch below would need on the order of 2^32 additions to ever
        // trigger - unreachable for any real folder. Kept for exact NanaZip source fidelity
        // anyway; see HashDigestAccumulatorTests for why only the 4-byte path is unit-tested.
        int highestNonZero = 0;
        for (int i = ExtraSize; i > 0; i--)
        {
            if (_bytes[_digestSize + i - 1] != 0)
            {
                highestNonZero = i;
                break;
            }
        }
        int extraLength = highestNonZero > 4 ? 8 : 4;
        string extra = HashHexToString(_bytes.AsSpan(_digestSize, extraLength));
        return $"{main}-{extra}";
    }

    // Mirrors HashCalc.cpp's HashHexToString: size<=8 (CRC-32's 4-byte digest, and this
    // accumulator's 4-or-8-byte extra-carry suffix) is upper case with byte order reversed from
    // storage order; size>8 (SHA-256's 32-byte digest) is lower case in storage byte order.
    private static string HashHexToString(ReadOnlySpan<byte> data)
    {
        if (data.Length > 8)
            return Convert.ToHexString(data).ToLowerInvariant();

        Span<byte> reversed = stackalloc byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            reversed[i] = data[data.Length - 1 - i];
        return Convert.ToHexString(reversed);
    }
}
