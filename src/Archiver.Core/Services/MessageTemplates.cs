using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// The English template of every <see cref="MessageCode"/> (T-F209) — the one place Core's
/// user-visible English text is written. Frontends translate these templates; their placeholders
/// ({0}, {1}) must stay the same.
/// </summary>
public static class MessageTemplates
{
    private static readonly Dictionary<MessageCode, string> Templates = new()
    {
        [MessageCode.None] = "{0}",

        [MessageCode.SourceNotFound] = "Source path does not exist: {0}",
        [MessageCode.CannotAccessFile] = "Cannot access file: {0}",
        [MessageCode.AccessDenied] = "Access denied: {0}",
        [MessageCode.LinkNotArchived] = "Symbolic links and NTFS junctions are not archived.",
        [MessageCode.FolderLinkNotFollowed] = "NTFS junctions and directory symbolic links are not followed during compression.",
        [MessageCode.ReparsePointNotArchived] = "Symbolic links and reparse points are not archived.",
        [MessageCode.ArchiveAlreadyExists] = "Archive '{0}' already exists at the destination and was skipped.",
        [MessageCode.CannotCreateArchive] = "Cannot create archive: {0}",
        [MessageCode.AccessDeniedCreatingArchive] = "Access denied creating archive: {0}",
        [MessageCode.UnexpectedError] = "Unexpected error: {0}",
        [MessageCode.UnknownArchivingError] = "Unknown error while compressing.",
        [MessageCode.EntryNameTooLong] = "Entry name is too long for a ZIP archive ({0} bytes as UTF-8; the maximum is {1}).",
        [MessageCode.NotEnoughSpaceToCompress] =
            "Not enough free disk space to compress this file: it is {0} bytes, but only {1} bytes are free.",
        [MessageCode.PasswordNotEntered] = "Archive was not created: no password was entered.",
        [MessageCode.PasswordEmpty] = "Archive was not created: the password is empty.",
        [MessageCode.PasswordUnsupportedCharacters] =
            "Archive was not created: the password may contain only English letters, digits, spaces and " +
            "ASCII punctuation; other ZIP tools such as 7-Zip cannot open an archive protected by any other characters.",
        [MessageCode.PasswordTooLong] =
            "Archive was not created: the password is longer than {0} characters, the most 7-Zip accepts for an AES-encrypted ZIP.",
        [MessageCode.PasswordOnlyForZip] = "Password protection is only available for ZIP archives.",
        [MessageCode.CreationFormatBlocked] = "Creating a {0} archive is blocked by Group Policy.",
        [MessageCode.TarCreationDisabled] = "tar.exe-based archive creation is disabled by Group Policy.",
        [MessageCode.TarCreationFailed] = "tar.exe failed to create archive: {0}",
        [MessageCode.TarNameNotRepresentable] =
            "The name '{0}' contains characters that tar.exe cannot handle safely on this system " +
            "(ANSI code page {1}). Use ZIP for this content.",
        [MessageCode.TarSignatureInvalid] = "tar.exe failed Authenticode signature verification; refusing to run it.",
        [MessageCode.TarSignatureVerificationFailed] = "'{0}' failed Authenticode signature verification.",

        [MessageCode.NotAnArchiveExtract] = "File is not a recognized archive format and cannot be extracted.",
        [MessageCode.NotAnArchiveTest] = "File is not a recognized archive format and cannot be tested.",
        [MessageCode.NotAnArchiveList] = "File is not a recognized archive format and cannot be listed.",
        [MessageCode.UnsupportedByZipEngine] = "{0} format is not supported. Only ZIP-based formats are supported.",
        [MessageCode.FormatBlocked] = "This archive format ({0}) is blocked by Group Policy.",
        [MessageCode.TarExtractionDisabled] = "tar.exe-based extraction is disabled by Group Policy.",
        [MessageCode.FormatNeedsNewerTar] =
            "{0} requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {1}) does not support it.",
        [MessageCode.FormatNotSupportedByTar] = "{0} is not supported by this system's tar.exe (version {1}).",
        [MessageCode.ArchiveFormatNotSupportedByTar] = "This archive format is not supported by this system's tar.exe (version {0}).",
        [MessageCode.NoTestCapability] = "tar-family archives have no test capability",
        [MessageCode.PasswordProtectedExtract] = "This archive is password-protected and cannot be extracted.",
        [MessageCode.PasswordProtectedTest] = "This archive is password-protected and cannot be tested.",
        [MessageCode.PasswordProtectedBrowse] = "This archive is password-protected and cannot be browsed.",
        [MessageCode.ZipCorrupted] = "File has ZIP signature but appears corrupted or incomplete.",
        [MessageCode.CannotExtractArchive] = "Cannot extract archive: {0}",
        [MessageCode.AccessDeniedExtractingArchive] = "Access denied extracting archive: {0}",
        [MessageCode.CannotReadArchive] = "Cannot read archive: {0}",
        [MessageCode.EntryNameCollision] =
            "Entry '{0}' has the same name as another entry once decoded; it is not extracted, since one would overwrite the other.",
        [MessageCode.LocalHeaderMismatch] =
            "{0} entries have a local header that does not match the central directory (first: '{1}'). Other programs may extract different names or data from this archive.",
        [MessageCode.EntryFailed] = "Entry '{0}': {1}",
        [MessageCode.EntryWrongPassword] = "Entry '{0}' could not be decrypted: wrong password.",
        [MessageCode.EntryAuthenticationFailed] = "Entry '{0}' could not be decrypted: authentication failed (corrupted or tampered).",
        [MessageCode.EntryUnsupportedEncryptedMethod] = "Entry '{0}' uses an unsupported compression method under encryption.",
        [MessageCode.InsufficientDiskSpace] =
            "Archive declares {0} bytes uncompressed, but the destination only has {1} bytes free. Extraction was blocked.",
        [MessageCode.ZipBombDeclined] =
            "Suspicious compression ratio ({0}:1, {1} bytes declared). Extraction was declined as a precaution against ZIP bombs.",
        [MessageCode.TarBombDeclined] =
            "Suspicious compression ratio ({0}:1, {1} bytes declared) across the whole archive. " +
            "Extraction was declined as a precaution against decompression bombs.",
        [MessageCode.DestinationFileLocked] = "Cannot write '{0}': destination file is locked by another process.",
        [MessageCode.AllEntriesSkipped] = "No entries were extracted from this archive — every entry was skipped.",
        [MessageCode.UnsafeEntryPath] = "Entry '{0}' has an unsafe path and was not extracted.",
        [MessageCode.EntryAlternateDataStream] = "Alternate Data Stream entry rejected for security.",
        [MessageCode.EntryReservedName] = "Entry name matches a reserved Windows device name and was skipped.",
        [MessageCode.EntryControlCharacters] = "Entry name contains control characters and was skipped.",
        [MessageCode.CannotExtractEntry] = "Cannot extract '{0}': {1}",
        [MessageCode.EntryThroughReparsePoint] = "Entry path traverses a reparse point (symlink or junction) and was skipped.",
        [MessageCode.FileExistsAtDestination] = "File already exists at destination.",
        [MessageCode.TarExtractionFailed] = "tar.exe extraction failed: {0}",
        [MessageCode.TarUnsafeEntryPath] = "Archive contains an unsafe entry path ('{0}') and cannot be safely extracted.",
        [MessageCode.TarListingInconsistent] = "Archive listing is inconsistent and cannot be safely extracted.",
        [MessageCode.TarSpecialEntry] =
            "Archive contains a symlink, hardlink, device, or other special entry and cannot be safely extracted.",
        [MessageCode.TarDuplicateCopiesNotExtracted] =
            "{0} more copies with this name were not extracted: only the first and the last copy can be taken out of this archive.",
        [MessageCode.TarSourceNameCollisionNotAdded] =
            "Another selected item has the same name, and this folder could not be added under a new name. Rename one of them and try again.",
        [MessageCode.ListingInconsistent] = "Archive listing is inconsistent.",
        [MessageCode.TarUnreadableNames] =
            "The archive contains file names tar.exe cannot represent on this system (code page {0}), " +
            "so it cannot be read with tar.exe here. Details: {1}",
        [MessageCode.SandboxSetupFailed] = "Sandbox setup failed: {0}",
        [MessageCode.ArchiveInUse] = "The archive is in use by another program: {0}",
        [MessageCode.CannotOpenArchive] = "Cannot open the archive: {0}",
        [MessageCode.CannotReopenArchive] = "Cannot reopen the archive (Win32 error {0}).",

        [MessageCode.HashFolderSkipped] = "Skipped: folder (only supported when a single folder is selected alone)",
        [MessageCode.HashLinkSkipped] = "Skipped: symbolic link or junction (not followed)",
        [MessageCode.HashFileChanged] = "The file changed size while it was being hashed: {0}",

        [MessageCode.NoAntivirusRegistered] = "No antivirus is registered to scan with.",
        [MessageCode.ScanSessionFailed] = "Could not start an antivirus scan session: {0}",
        [MessageCode.ScanCannotReadArchive] = "Could not read archive: {0}",
        [MessageCode.ScanCannotReadEntry] = "Could not read entry: {0}",
        [MessageCode.ScanCannotExtract] = "Could not extract archive for scanning: {0}",
        [MessageCode.ScanRemovedOrBlocked] = "Removed or blocked before Pakko could scan it directly.",
        [MessageCode.ScanEntryFailed] = "The antivirus scan of this entry failed: {0}",
        [MessageCode.ScanEntryIntegrityFailed] =
            "Decrypted content failed its integrity check (wrong password or corrupted entry) and was not reported clean.",
        [MessageCode.ScanEntryPasswordProtected] = "Entry is password-protected and was not scanned.",
        [MessageCode.ScanEntryWrongPassword] = "Entry is password-protected with a different password and was not scanned.",
        [MessageCode.ScanEntryUnsupportedMethod] = "Entry uses an unsupported compression method under encryption and was not scanned.",
        [MessageCode.ScanEntryAuthenticationFailed] = "Entry failed decryption authentication (corrupted or tampered) and was not scanned.",
        [MessageCode.ScanEntryTooLarge] = "Entry is larger than {0} MiB and was not scanned.",
    };

    /// <summary>Every code with a template — all of <see cref="MessageCode"/> except <see cref="MessageCode.None"/>.</summary>
    public static IEnumerable<MessageCode> Codes => Templates.Keys.Where(code => code != MessageCode.None);

    /// <summary>The English template for <paramref name="code"/>.</summary>
    public static string English(MessageCode code) =>
        Templates.TryGetValue(code, out string? template) ? template : Templates[MessageCode.None];
}
