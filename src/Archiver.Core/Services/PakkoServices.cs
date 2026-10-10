using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// The one place a frontend without a DI container (Archiver.Shell, Archiver.CLI) gets its Core
/// services from (T-F261), so every service is built with the same Group Policy — a hand-built
/// composition root per command is how the CLI once shipped a policy-less listing. The tar.exe
/// capability probe runs at most once per instance (T-F85), only when an operation meets a
/// tar-family archive that policy allows (T-F350), and not at all when Group Policy disables
/// tar.exe.
/// </summary>
public sealed class PakkoServices
{
    private readonly Lazy<Task<TarCapabilities>> _tarCapabilities;

    private PakkoServices(GroupPolicyOptions policy, IArchiveService archiveService, ITarService tarService)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
        ArchiveService = archiveService;
        TarService = tarService;
        CreationRouter = new ArchiveCreationRouter(archiveService, tarService, policy);
        RecoveryService = new RecoveryService(policy, CreateExtractionRouterAsync);
        _tarCapabilities = new Lazy<Task<TarCapabilities>>(tarService.DetectCapabilitiesAsync);
    }

    /// <summary>The Group Policy every service here was built with.</summary>
    public GroupPolicyOptions Policy { get; }

    /// <summary>The ZIP engine.</summary>
    public IArchiveService ArchiveService { get; }

    /// <summary>The tar.exe engine.</summary>
    public ITarService TarService { get; }

    /// <summary>Archive creation; needs no tar.exe probe.</summary>
    public IArchiveCreationRouter CreationRouter { get; }

    /// <summary>Repair from PAR2 recovery data (T-F275 step 4); needs no tar.exe probe.</summary>
    public IRecoveryService RecoveryService { get; }

    /// <summary>Builds the real engines under <paramref name="policy"/>.</summary>
    public static PakkoServices Create(GroupPolicyOptions policy) =>
        new(policy, new ZipArchiveService(policy), new TarSandboxedService(policy));

    // Test seam: hand-rolled engines, so a test can fail on any tar.exe call.
    internal static PakkoServices Create(GroupPolicyOptions policy, IArchiveService archiveService, ITarService tarService) =>
        new(policy, archiveService, tarService);

    /// <summary>What tar.exe can read — probed on first use, then cached.</summary>
    public Task<TarCapabilities> GetTarCapabilitiesAsync() => _tarCapabilities.Value;

    /// <summary>Extraction and testing.</summary>
    public Task<IExtractionRouter> CreateExtractionRouterAsync() =>
        Task.FromResult<IExtractionRouter>(new ExtractionRouter(ArchiveService, TarService, GetTarCapabilitiesAsync, Policy));

    /// <summary>Listing one archive at a time.</summary>
    public Task<IArchiveListingRouter> CreateListingRouterAsync() =>
        Task.FromResult<IArchiveListingRouter>(new ArchiveListingRouter(ArchiveService, TarService, GetTarCapabilitiesAsync, Policy));

    /// <summary>AMSI scanning, wired to the real AMSI provider.</summary>
    [SupportedOSPlatform("windows")]
    public Task<IAntivirusScanService> CreateScanServiceAsync() =>
        Task.FromResult<IAntivirusScanService>(new AntivirusScanService(GetTarCapabilitiesAsync, Policy));
}
