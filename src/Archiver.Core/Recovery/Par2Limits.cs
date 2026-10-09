namespace Archiver.Core.Recovery;

/// <summary>
/// The bounds Pakko holds every PAR2 set to, read or written (T-F275). The writer stays inside
/// them by construction (a test checks it over the whole size range), so the reader can refuse
/// anything outside without refusing a set Pakko made.
/// </summary>
internal static class Par2Limits
{
    /// <summary>The specification's limit on input slices across a recovery set.</summary>
    internal const int MaxInputSlices = 32768;

    /// <summary>Largest slice accepted: 1 GiB. With <see cref="MaxInputSlices"/>, a 32 TiB file.</summary>
    internal const long MaxSliceSize = 1L << 30;

    /// <summary>Largest packet other than a recovery slice: room for an IFSC packet of
    /// <see cref="MaxInputSlices"/> entries and a Main packet listing tens of thousands of files.</summary>
    internal const long MaxSmallPacketLength = 1L << 20;

    /// <summary>The number of input slices the writer aims for, as par2cmdline does by default.</summary>
    internal const int TargetInputSlices = 2000;

    /// <summary>Largest repair, in matrix entries: missing slices × (slices + missing slices).
    /// About 128 MiB for each of the two matrices; a default set of 2000 slices stays inside it
    /// with every slice missing.</summary>
    internal const long MaxRepairMatrixEntries = 1L << 26;
}
