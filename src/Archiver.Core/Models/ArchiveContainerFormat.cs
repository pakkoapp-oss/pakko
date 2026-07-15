namespace Archiver.Core.Models;

// The container/compression format to CREATE a new archive as. Deliberately separate from
// ArchiveFormat (Archiver.Core/Models/ArchiveFormat.cs), which is detection-only (magic-byte
// sniffing for extraction routing) and includes read-only formats (Rar, SevenZip) that can never
// appear here.
public enum ArchiveContainerFormat
{
    Zip,
    Tar,
    TarGz,
    TarBz2,
    TarXz,
    TarZst,
    TarLzma,
}
