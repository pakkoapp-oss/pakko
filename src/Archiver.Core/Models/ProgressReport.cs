namespace Archiver.Core.Models;

/// <summary>
/// Progress snapshot reported during archive/extract operations.
/// </summary>
public sealed class ProgressReport
{
    /// <summary>0–100. Byte-accurate percentage.</summary>
    public int Percent { get; init; }

    /// <summary>Total bytes transferred so far across all files.</summary>
    public long BytesTransferred { get; init; }

    /// <summary>Total bytes expected for the entire operation.</summary>
    public long TotalBytes { get; init; }
}
