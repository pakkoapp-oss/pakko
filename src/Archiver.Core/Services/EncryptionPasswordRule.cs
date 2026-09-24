namespace Archiver.Core.Services;

/// <summary>Why <see cref="EncryptionPasswordRule.Check"/> refused a password for a new encrypted ZIP.</summary>
public enum EncryptionPasswordProblem
{
    /// <summary>The password is acceptable.</summary>
    None,

    /// <summary>The password is an empty string.</summary>
    Empty,

    /// <summary>A character outside printable ASCII (0x20-0x7F) — 7-Zip would report "Wrong password".</summary>
    UnsupportedCharacters,

    /// <summary>Longer than <see cref="EncryptionPasswordRule.MaxLength"/>.</summary>
    TooLong
}

/// <summary>
/// T-F193: which passwords Pakko accepts when <em>creating</em> an AES-256 ZIP. Public so every
/// frontend can refuse bad input inside its own prompt with localized text, while
/// <see cref="ZipArchiveService.ArchiveAsync"/> enforces the same rule as the last line of defence.
/// Reading (T-F189) accepts any password.
/// </summary>
public static class EncryptionPasswordRule
{
    /// <summary>
    /// 7-Zip's own AES limit (WzAes.cpp, kPasswordSizeMax) — it refuses a longer password even when
    /// reading, so an archive protected by one could never be opened there.
    /// </summary>
    public const int MaxLength = 99;

    /// <summary>
    /// Mirrors 7-Zip's creation rule (ZipHandlerOut.cpp, IsSimpleAsciiString): 7-Zip decodes a ZIP
    /// password through the ANSI code page, not UTF-8, so any other character produces an archive
    /// 7-Zip/NanaZip cannot open. Characters are checked before length.
    /// </summary>
    public static EncryptionPasswordProblem Check(string password)
    {
        if (password.Length == 0)
            return EncryptionPasswordProblem.Empty;
        if (password.Any(c => c < 0x20 || c > 0x7F))
            return EncryptionPasswordProblem.UnsupportedCharacters;
        if (password.Length > MaxLength)
            return EncryptionPasswordProblem.TooLong;
        return EncryptionPasswordProblem.None;
    }
}
