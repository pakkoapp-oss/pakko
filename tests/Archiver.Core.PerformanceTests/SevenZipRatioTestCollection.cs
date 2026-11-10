namespace Archiver.Core.PerformanceTests;

/// <summary>
/// The classes that time Pakko against 7za. xUnit runs a collection with parallelization disabled
/// on its own, after the parallel ones, so no other test of this project compresses, hashes or
/// writes PAR2 while a ratio is measured. Before (2026-11-10), Hash/ManyFilesAndFolders read
/// 0.43-0.51 alone and up to 0.90 beside CompressionPerformanceTests, against a 0.795 ceiling.
/// </summary>
// CA1711: xUnit's own CollectionDefinition marker-class convention names these "XCollection"
// (see docs/CONVENTIONS.md) — no name avoiding the "Collection" suffix stays idiomatic here.
#pragma warning disable CA1711
[CollectionDefinition("SevenZipRatios", DisableParallelization = true)]
public sealed class SevenZipRatioTestCollection
{
}
#pragma warning restore CA1711
