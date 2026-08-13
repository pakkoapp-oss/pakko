namespace Archiver.Core.Models;

/// <summary>T-F128: the two hash algorithms exposed via the Explorer context menu's "Хеш-суми" submenu.</summary>
public enum HashAlgorithmKind
{
    /// <summary>CRC-32 — also usable for a real single-folder DataSum/NamesSum (see FileHashService).</summary>
    Crc32,

    /// <summary>SHA-256.</summary>
    Sha256,
}
