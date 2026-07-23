namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Forces every test class that drives the real Win32 AppContainer/Job Object/quarantine ACL
/// machinery to run sequentially relative to each other (xUnit still runs this collection in
/// parallel with unrelated collections). Root-causes the CI flakiness documented in CLAUDE.md's
/// "Known test gaps" section — concurrent AppContainer profile/Job Object calls across test
/// classes were racing under xUnit's default parallel-by-class execution, not a real product bug.
/// </summary>
[CollectionDefinition("TarSandbox", DisableParallelization = true)]
public sealed class TarSandboxCollection
{
}
