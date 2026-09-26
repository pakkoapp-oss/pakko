using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>
/// The Core services an Explorer command needs, as factories (T-F268) — so tests can build a
/// ZIP-only router without starting tar.exe for capability detection.
/// </summary>
internal sealed class ShellServices
{
    public required Func<Task<IExtractionRouter>> CreateExtractionRouterAsync { get; init; }
    public required Func<IArchiveCreationRouter> CreateArchiveCreationRouter { get; init; }
    public required Func<IArchiveService> CreateArchiveService { get; init; }
    public required Func<Task<AntivirusScanService>> CreateScanServiceAsync { get; init; }
    public required Func<LaunchOperation, IReadOnlyList<string>, AppLaunchResult> LaunchApp { get; init; }

    public static ShellServices Create(GroupPolicyOptions policy) => new()
    {
        // T-F85: DetectCapabilitiesAsync spawns tar.exe, so it runs once per Explorer invocation
        // and the result is shared across every archive in the selection.
        CreateExtractionRouterAsync = async () =>
        {
            var tarService = new TarSandboxedService(policy);
            var capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);
            return new ExtractionRouter(new ZipArchiveService(policy), tarService, capabilities, policy);
        },
        CreateArchiveCreationRouter = () =>
            new ArchiveCreationRouter(new ZipArchiveService(policy), new TarSandboxedService(policy), policy),
        CreateArchiveService = () => new ZipArchiveService(policy),
        CreateScanServiceAsync = async () =>
        {
            var tarService = new TarSandboxedService(policy);
            var capabilities = await tarService.DetectCapabilitiesAsync().ConfigureAwait(false);
            return new AntivirusScanService(capabilities, policy);
        },
        LaunchApp = AppLauncher.Launch,
    };
}
