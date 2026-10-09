using System.Security.Cryptography;

namespace Archiver.Core.Recovery;

/// <summary>
/// The one place PAR2 code reaches MD5 (T-F275). PAR 2.0 fixes MD5 as its checksum for packets,
/// slices and files; it guards against accidental damage, not against an attacker (the author of
/// a PAR2 set sets both the data and the hashes). Kept behind one helper so that, should the FIPS
/// policy make the BCL refuse MD5, an own RFC 1321 implementation replaces one file.
/// </summary>
internal static class Par2Md5
{
    internal const int Size = 16;

#pragma warning disable CA5351 // PAR 2.0 mandates MD5 (docs/CONVENTIONS.md, Won't-Fix)
    internal static byte[] Hash(ReadOnlySpan<byte> data) => MD5.HashData(data); // NOSONAR: S4790 — PAR 2.0 mandates MD5

    internal static IncrementalHash Create() => IncrementalHash.CreateHash(HashAlgorithmName.MD5); // NOSONAR: S4790 — PAR 2.0 mandates MD5
#pragma warning restore CA5351
}
