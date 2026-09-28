namespace Archiver.Core.Models;

/// <summary>
/// How one archive entry is encrypted, read from the ZIP headers without a password (T-F199).
/// <see cref="Unknown"/> means the entry is marked encrypted but the method could not be read
/// (a malformed WinZip AES record, PKWARE strong encryption).
/// </summary>
public enum EntryEncryption
{
    None,
    ZipCrypto,
    Aes128,
    Aes192,
    Aes256,
    Unknown,
}
