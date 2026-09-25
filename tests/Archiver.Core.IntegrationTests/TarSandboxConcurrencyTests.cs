using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F244 item 1: tar operations running at the same time. CreateAppContainerProfile on the
/// existing profile, called concurrently, failed with 0x800703FA/0x8000FFFF — the long-standing
/// "Sandbox setup failed" flake across test projects, and a real failure when two operations
/// start together (for example the App's browse and an Explorer extract).
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxConcurrencyTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task EnsureExists_ManyConcurrentCallers_NeverFails()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                try { new AppContainerProfile(AppContainerProfile.ProductionProfileName).EnsureExists(); }
                catch (InvalidOperationException ex) { failures.Add(ex.Message); }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        failures.Should().BeEmpty();
    }
}
