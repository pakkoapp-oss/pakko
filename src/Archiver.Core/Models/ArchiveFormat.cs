namespace Archiver.Core.Models;

public enum ArchiveFormat
{
    Unknown,
    Zip,
    Tar,
    GZip,
    Bz2,
    Xz,
    Zstd,
    Lzma,
    Rar,
    SevenZip,

    // T-F442: read-only formats tar.exe opens beyond the tar family, appended so no stored value moves.

    /// <summary>An ISO 9660 disc image (Joliet, Rock Ridge).</summary>
    Iso,

    /// <summary>A Microsoft cabinet.</summary>
    Cab,

    /// <summary>A cpio archive, odc or newc.</summary>
    Cpio,

    /// <summary>A Unix ar archive, the shell of a .deb.</summary>
    Ar,

    /// <summary>A Unix compress (.Z) stream, usually around a tar.</summary>
    Compress,

    /// <summary>An lzip stream, usually around a tar.</summary>
    Lzip,

    /// <summary>A UDF disc image: tar.exe (libarchive) reads no UDF, at most an ISO 9660 stub beside it.</summary>
    Udf,
}
