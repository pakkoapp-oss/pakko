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

    // Eight workers, each listing and extracting a .tar.gz and a .rar fifteen times through its
    // own scopes — the stdin-handle path (T-F233) under concurrency.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task Scopes_ManyInParallel_AllListAndExtract()
    {
        string rar = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "valid.rar");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            string gz = Path.Combine(_temp.Path, $"w{worker}.tar.gz");
            ExternalTarFixtureBuilder.CreateCompressedTar(gz, "-czf", [("a.txt", "x" + worker)]);
            for (int i = 0; i < 15; i++)
            {
                foreach (string archive in new[] { gz, rar })
                {
                    try
                    {
                        using var scope = await TarSandboxScope.CreateAsync(archive, needsOutputDir: true, CancellationToken.None);
                        var list = await scope.ListAsync(verbose: false, CancellationToken.None);
                        if (list.ExitCode != 0)
                            failures.Add($"list {Path.GetFileName(archive)}: {list.StdErr}");
                        var extract = await scope.ExtractAsync(null, CancellationToken.None);
                        if (extract.ExitCode != 0)
                            failures.Add($"extract {Path.GetFileName(archive)}: {extract.StdErr}");
                    }
                    catch (Exception ex) when (ex is IOException or SandboxSetupException)
                    {
                        failures.Add($"{Path.GetFileName(archive)}: {ex.Message}");
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        failures.Should().BeEmpty();
    }
}
