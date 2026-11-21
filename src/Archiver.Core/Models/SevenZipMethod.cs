namespace Archiver.Core.Models;

/// <summary>
/// T-F342: how a new 7z archive's data is compressed. The methods libarchive's 7z writer has and
/// both 7-Zip and this system's tar.exe read back; PPMd and Zstandard are left out (tar.exe cannot
/// read its own PPMd archive of incompressible data, and 7-Zip has no Zstandard).
/// </summary>
public enum SevenZipMethod
{
    /// <summary>LZMA2, 7-Zip's own default.</summary>
    Lzma2,

    /// <summary>LZMA.</summary>
    Lzma,

    /// <summary>BZip2.</summary>
    BZip2,

    /// <summary>Deflate.</summary>
    Deflate,

    /// <summary>Stored without compression.</summary>
    Copy,

    /// <summary>
    /// T-F565: Zstandard, written by libarchive 3.8 and later where tar.exe was seen writing it
    /// (<see cref="Interfaces.ITarService.CanCreateSevenZipZstdAsync"/>). 7-Zip itself cannot open
    /// it; NanaZip and tar.exe 3.7 or later can.
    /// </summary>
    Zstd,
}
