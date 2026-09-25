namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Forces every test class that drives the real Win32 AppContainer/Job Object/quarantine ACL
/// machinery to run sequentially relative to each other (xUnit still runs this collection in
/// parallel with unrelated collections). Root-causes the CI flakiness documented in CLAUDE.md's
/// "Known test gaps" section — concurrent AppContainer profile/Job Object calls across test
/// classes were racing under xUnit's default parallel-by-class execution. Correction
/// (2026-09-25, T-F244 item 1): part of it was a real product race — concurrent
/// CreateAppContainerProfile calls on the existing profile failed; see TarSandboxConcurrencyTests.
/// </summary>
// CA1711: xUnit's own CollectionDefinition marker-class convention names these "XCollection"
// (see docs/CONVENTIONS.md) — no name avoiding the "Collection" suffix stays idiomatic here.
#pragma warning disable CA1711
[CollectionDefinition("TarSandbox", DisableParallelization = true)]
public sealed class TarSandboxTestCollection
{
}
#pragma warning restore CA1711
