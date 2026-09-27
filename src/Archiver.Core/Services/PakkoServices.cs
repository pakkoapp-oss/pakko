using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// The one place a frontend without a DI container (Archiver.Shell, Archiver.CLI) gets its Core
/// services from (T-F261), so every service is built with the same Group Policy — a hand-built
/// composition root per command is how the CLI once shipped a policy-less listing. The tar.exe
/// capability probe runs at most once per instance (T-F85), and not at all when Group Policy
/// disables tar.exe.
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

    /// <summary>Builds the real engines under <paramref name="policy"/>.</summary>
    public static PakkoServices Create(GroupPolicyOptions policy) =>
        new(policy, new ZipArchiveService(policy), new TarSandboxedService(policy));

    // Test seam: hand-rolled engines, so a test can fail on any tar.exe call.
    internal static PakkoServices Create(GroupPolicyOptions policy, IArchiveService archiveService, ITarService tarService) =>
        new(policy, archiveService, tarService);

    /// <summary>What tar.exe can read — probed on first use, then cached.</summary>
    public Task<TarCapabilities> GetTarCapabilitiesAsync() => _tarCapabilities.Value;

    /// <summary>Extraction and testing.</summary>
    public async Task<IExtractionRouter> CreateExtractionRouterAsync() =>
        new ExtractionRouter(ArchiveService, TarService, await GetTarCapabilitiesAsync().ConfigureAwait(false), Policy);

    /// <summary>Listing one archive at a time.</summary>
    public async Task<IArchiveListingRouter> CreateListingRouterAsync() =>
        new ArchiveListingRouter(ArchiveService, TarService, await GetTarCapabilitiesAsync().ConfigureAwait(false));

    /// <summary>AMSI scanning, wired to the real AMSI provider.</summary>
    [SupportedOSPlatform("windows")]
    public async Task<IAntivirusScanService> CreateScanServiceAsync() =>
        new AntivirusScanService(await GetTarCapabilitiesAsync().ConfigureAwait(false), Policy);
}
