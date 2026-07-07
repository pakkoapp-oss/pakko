namespace Archiver.Core.Models;

/// <summary>
/// Details surfaced to a confirmation callback when an archive's declared uncompressed size
/// exceeds the compression-ratio threshold (T-F94). See ArchiveEntrySecurity.EvaluateCompressionBombAsync.
/// </summary>
public sealed record CompressionBombWarning
{
    public string ArchivePath { get; init; } = string.Empty;
    public long DeclaredUncompressedSize { get; init; }
    public long CompressedSize { get; init; }
    public long Ratio => CompressedSize > 0 ? DeclaredUncompressedSize / CompressedSize : 0;
}
