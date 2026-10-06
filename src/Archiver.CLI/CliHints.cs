using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>The way forward a hint line offers (T-F221 items 2-3).</summary>
public enum CliHint
{
    None,

    /// <summary>The archive is encrypted and no password was given.</summary>
    GivePassword,

    /// <summary>An existing file was kept because nothing else was chosen.</summary>
    OverwriteSwitches,

    /// <summary>
    /// Every entry was skipped. ZIP lists no per-file conflict skip, so this means a conflict only
    /// when nothing else in the result explains it (T-F293: after a CRC failure it does not).
    /// </summary>
    OverwriteSwitchesWhenUnexplained,
}

/// <summary>
/// T-F296 item 2: hints keyed on the cause code of each error and skip, not guessed from a
/// symptom. <see cref="CauseTable"/> names every <see cref="MessageCode"/>, so a new code cannot
/// slip through without a decision (CliHintsTests checks it).
/// </summary>
public static class CliHints
{
    private const string PasswordLine = "pakko: hint: give the password with -p<password>";
    private const string OverwriteLine = "pakko: hint: existing files were kept; -aoa overwrites them, -aou renames the extracted ones";

    private static readonly Dictionary<MessageCode, CliHint> Hinted = new()
    {
        [MessageCode.PasswordProtectedExtract] = CliHint.GivePassword,
        [MessageCode.PasswordProtectedTest] = CliHint.GivePassword,
        [MessageCode.FileExistsAtDestination] = CliHint.OverwriteSwitches,
        [MessageCode.AllEntriesSkipped] = CliHint.OverwriteSwitchesWhenUnexplained,
    };

    // Listed one by one on purpose: a code added to MessageCode fails CliHintsTests until it is
    // placed here or above.
    private static readonly MessageCode[] NoHint =
    [
        MessageCode.None, MessageCode.SourceNotFound, MessageCode.NothingToArchive, MessageCode.CannotAccessFile, MessageCode.AccessDenied,
        MessageCode.LinkNotArchived, MessageCode.FolderLinkNotFollowed, MessageCode.ReparsePointNotArchived, MessageCode.ArchiveAlreadyExists,
        MessageCode.CannotCreateArchive, MessageCode.AccessDeniedCreatingArchive, MessageCode.UnexpectedError, MessageCode.UnknownArchivingError,
        MessageCode.EntryNameTooLong, MessageCode.NotEnoughSpaceToCompress, MessageCode.PasswordNotEntered, MessageCode.PasswordEmpty,
        MessageCode.PasswordUnsupportedCharacters, MessageCode.PasswordTooLong, MessageCode.PasswordOnlyForZip, MessageCode.CreationFormatBlocked,
        MessageCode.TarCreationDisabled, MessageCode.TarCreationFailed, MessageCode.TarNameNotRepresentable, MessageCode.TarSignatureInvalid,
        MessageCode.TarSignatureVerificationFailed, MessageCode.NotAnArchiveExtract, MessageCode.NotAnArchiveTest, MessageCode.NotAnArchiveList,
        MessageCode.UnsupportedByZipEngine, MessageCode.FormatBlocked, MessageCode.TarExtractionDisabled, MessageCode.FormatNeedsNewerTar,
        MessageCode.FormatNotSupportedByTar, MessageCode.ArchiveFormatNotSupportedByTar, MessageCode.NoTestCapability, MessageCode.PasswordProtectedBrowse, MessageCode.RememberedPasswordDoesNotFit,
        MessageCode.ZipCorrupted, MessageCode.CannotExtractArchive, MessageCode.AccessDeniedExtractingArchive, MessageCode.CannotReadArchive,
        MessageCode.EntryNameCollision, MessageCode.EntryFailed, MessageCode.EntryWrongPassword, MessageCode.EntryAuthenticationFailed,
        MessageCode.EntryUnsupportedEncryptedMethod, MessageCode.InsufficientDiskSpace, MessageCode.ZipBombDeclined, MessageCode.TarBombDeclined,
        MessageCode.DestinationFileLocked, MessageCode.UnsafeEntryPath, MessageCode.EntryAlternateDataStream, MessageCode.EntryReservedName,
        MessageCode.EntryControlCharacters, MessageCode.CannotExtractEntry, MessageCode.EntryThroughReparsePoint, MessageCode.TarExtractionFailed,
        MessageCode.TarUnsafeEntryPath, MessageCode.TarListingInconsistent, MessageCode.TarSpecialEntry, MessageCode.TarDuplicateCopiesNotExtracted,
        MessageCode.TarSourceNameCollisionNotAdded, MessageCode.ListingInconsistent, MessageCode.TarUnreadableNames, MessageCode.SandboxSetupFailed,
        MessageCode.ArchiveInUse, MessageCode.CannotOpenArchive, MessageCode.CannotReopenArchive, MessageCode.HashFolderSkipped,
        MessageCode.HashLinkSkipped, MessageCode.HashFileChanged, MessageCode.NoAntivirusRegistered, MessageCode.ScanSessionFailed,
        MessageCode.ScanCannotReadArchive, MessageCode.ScanCannotReadEntry, MessageCode.ScanCannotExtract, MessageCode.ScanRemovedOrBlocked,
        MessageCode.ScanEntryFailed, MessageCode.ScanEntryIntegrityFailed, MessageCode.ScanEntryPasswordProtected, MessageCode.ScanEntryWrongPassword,
        MessageCode.ScanEntryUnsupportedMethod, MessageCode.ScanEntryAuthenticationFailed, MessageCode.ScanEntryTooLarge, MessageCode.LocalHeaderMismatch,
        MessageCode.SystemInvalidName, MessageCode.SystemAccessDenied, MessageCode.SystemSharingViolation, MessageCode.SystemDiskFull,
        MessageCode.ContentCrcMismatch, MessageCode.ContentLargerThanDeclared, MessageCode.ContentSmallerThanDeclared,
        MessageCode.EntryDataTruncated, MessageCode.PasswordProtectedFormatNotSupported,
    ];

    /// <summary>Every code and its hint; <see cref="CliHint.None"/> is a decision too.</summary>
    public static IReadOnlyDictionary<MessageCode, CliHint> CauseTable { get; } =
        NoHint.ToDictionary(code => code, _ => CliHint.None).Concat(Hinted).ToDictionary();

    /// <summary>The hint lines for a result's error and skip codes, each line at most once.</summary>
    public static IReadOnlyList<string> For(
        IReadOnlyList<MessageCode?> errorCodes, IReadOnlyList<MessageCode?> skipCodes, bool passwordGiven, bool keptExistingByDefault)
    {
        var hints = new HashSet<CliHint>();
        foreach (MessageCode? code in errorCodes.Concat(skipCodes))
        {
            if (code is { } known)
                hints.Add(CauseTable[known]);
        }

        bool unexplained = errorCodes.Count == 0 && skipCodes.All(c => c == MessageCode.AllEntriesSkipped);
        var lines = new List<string>();
        if (!passwordGiven && hints.Contains(CliHint.GivePassword))
            lines.Add(PasswordLine);
        if (keptExistingByDefault
            && (hints.Contains(CliHint.OverwriteSwitches) || (unexplained && hints.Contains(CliHint.OverwriteSwitchesWhenUnexplained))))
            lines.Add(OverwriteLine);
        return lines;
    }
}
