namespace Archiver.Core.Models;

// The container/compression format to CREATE a new archive as. Deliberately separate from
// ArchiveFormat (Archiver.Core/Models/ArchiveFormat.cs), which is detection-only (magic-byte
// sniffing for extraction routing) and includes a read-only format (Rar) that can never appear
// here. SevenZip (T-F342) is written by tar.exe's --format=7zip where this system's tar.exe has
// that writer (ITarService.CanCreateSevenZipAsync).
public enum ArchiveContainerFormat
{
    Zip,
    Tar,
    TarGz,
    TarBz2,
    TarXz,
    TarZst,
    TarLzma,
    SevenZip,

    // T-F413: one file compressed on its own, no tar inside (7-Zip's -tgzip, -tbzip2, -txz),
    // written by tar.exe's raw format. The CLI's only; the App and Explorer do not offer them.
    GZip,
    BZip2,
    Xz,
}
