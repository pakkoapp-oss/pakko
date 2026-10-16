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
    public required Func<Task<IAntivirusScanService>> CreateScanServiceAsync { get; init; }
    public required Func<IRecoveryService> CreateRecoveryService { get; init; }
    public required Func<LaunchOperation, IReadOnlyList<string>, AppLaunchResult> LaunchApp { get; init; }

    // T-F342: leaves what the 7z writer probe found where the Explorer menu reads it.
    public Func<Task> RecordSevenZipWriterAsync { get; init; } = () => Task.CompletedTask;

    // T-F403: after any command, what tar.exe reads goes to the Explorer menu's snapshot when that
    // is missing or stale, so the menu learns it without the App having run.
    public Func<Task> RecordTarCapabilitiesIfStaleAsync { get; init; } = () => Task.CompletedTask;

    // T-F261: every service comes from Core's one factory, built with the one loaded policy.
    // T-F85: the factory probes tar.exe at most once per Explorer invocation.
    public static ShellServices Create(GroupPolicyOptions policy)
    {
        var core = PakkoServices.Create(policy);
        return new()
        {
            CreateExtractionRouterAsync = core.CreateExtractionRouterAsync,
            CreateArchiveCreationRouter = () => core.CreationRouter,
            CreateScanServiceAsync = core.CreateScanServiceAsync,
            CreateRecoveryService = () => core.RecoveryService,
            LaunchApp = AppLauncher.Launch,
            RecordSevenZipWriterAsync = async () => TarCapabilitySnapshot.TryRecord(
                policy, await core.TarService.AskSevenZipWriterAsync().ConfigureAwait(false),
                await core.GetTarCapabilitiesAsync().ConfigureAwait(false)),
            RecordTarCapabilitiesIfStaleAsync = () => TarCapabilitySnapshot.RecordIfStaleAsync(policy, core.GetTarCapabilitiesAsync),
        };
    }
}
