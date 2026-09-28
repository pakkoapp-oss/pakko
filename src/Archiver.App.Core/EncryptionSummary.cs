using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F199 (browse mode): the "encrypted" badge and info panel for an archive, read from the
/// listing without a password (T-F202: nothing showed the archive was encrypted until a password
/// was asked for).
/// </summary>
/// <param name="EncryptedFiles">Files that are encrypted.</param>
/// <param name="TotalFiles">Files in the archive (folders excluded).</param>
/// <param name="Badge">The weakest method present — the claim that holds for every encrypted file — or null when nothing is encrypted or the format cannot say.</param>
/// <param name="HasAe2">An AES entry is AE-2: its CRC-32 column is empty on purpose (the HMAC authenticates it).</param>
public sealed record EncryptionSummary(int EncryptedFiles, int TotalFiles, EntryEncryption? Badge, bool HasAe2)
{
    /// <summary>True when the badge and info panel show.</summary>
    public bool IsEncrypted => EncryptedFiles > 0;

    /// <summary>Summarizes a listing.</summary>
    public static EncryptionSummary Of(IReadOnlyList<ArchiveEntryInfo> entries)
    {
        ArchiveEntryInfo[] files = [.. entries.Where(e => !e.IsDirectory)];
        ArchiveEntryInfo[] encrypted = [.. files.Where(e => e.Encryption is { } kind && kind != EntryEncryption.None)];
        EntryEncryption? badge = encrypted.Length == 0 ? null : encrypted.Select(e => e.Encryption!.Value).MinBy(Strength);
        return new EncryptionSummary(encrypted.Length, files.Length, badge, encrypted.Any(e => e.AesVersion == 2));
    }

    // A mixed archive is only as strong as its weakest entry; an unnamed method ranks lowest.
    private static int Strength(EntryEncryption kind) => kind switch
    {
        EntryEncryption.Aes256 => 5,
        EntryEncryption.Aes192 => 4,
        EntryEncryption.Aes128 => 3,
        EntryEncryption.ZipCrypto => 2,
        _ => 1,
    };
}
