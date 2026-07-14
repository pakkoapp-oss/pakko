namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Disposable orchestration tying the AppContainer profile, quarantine ACLs, archive staging, and
/// Job Object into one scope per archive operation. <see cref="RunAsync"/> is the single choke
/// point every sandboxed tar.exe launch goes through — used by both T-F49's whole-archive
/// pre-scan and, only if that passes, the extraction itself, within the same scope (not two
/// separate scopes), and by ListEntriesAsync with <c>needsOutputDir: false</c> (no "out\" folder
/// or ACE at all, since listing never writes).
/// </summary>
internal sealed class TarSandboxScope : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";
    private const long RamLimitBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan CpuTimeLimit = TimeSpan.FromMinutes(5);

    private readonly SafeSidHandle _sid;
    private readonly SecurityCapabilitiesAttributeList _securityCapabilities;
    private readonly string _quarantineRoot;

    public string StagedArchivePath { get; }

    /// <summary>Null when this scope was created with needsOutputDir: false (listing only).</summary>
    public string? OutputDirectory { get; }

    /// <summary>The operation-scoped quarantine directory (contains "in\" and, if present, "out\") — deleted whole by Dispose().</summary>
    public string QuarantineRoot => _quarantineRoot;

    private TarSandboxScope(
        SafeSidHandle sid,
        SecurityCapabilitiesAttributeList securityCapabilities,
        string quarantineRoot,
        string stagedArchivePath,
        string? outputDirectory)
    {
        _sid = sid;
        _securityCapabilities = securityCapabilities;
        _quarantineRoot = quarantineRoot;
        StagedArchivePath = stagedArchivePath;
        OutputDirectory = outputDirectory;
    }

    // Rooted under %TEMP%, not "same disk as destination" as TASKS.md's original flow described —
    // an AppContainer token has no bypass-traverse-checking privilege, so FILE_TRAVERSE is
    // enforced on every ancestor directory down to "in\"/"out\". A fresh directory created as a
    // sibling of the user's arbitrary destination folder (e.g. inside Desktop/Documents/a network
    // share) sits under an ancestor chain Pakko doesn't control and can't grant traverse on
    // without touching folders it doesn't own. %TEMP% itself is already AppContainer-traversable
    // (confirmed empirically — QuarantineAclTests grants traverse only on its own quarantine root,
    // one level below %TEMP%, and that already succeeds), so rooting here needs traverse grants
    // on only the two levels Pakko itself creates. See DECISIONS.md's T-F52 entry for the full
    // empirical trace (a nested-one-level-deeper test failed until this was found).
    private static readonly string SandboxParentDirectory = Path.Combine(Path.GetTempPath(), "PakkoTarSandbox");

    /// <summary>
    /// Sets up a fresh quarantine "in\" (and, if needed, "out\") folder pair under a new
    /// operation-scoped directory beneath <see cref="SandboxParentDirectory"/>, ACLs every level
    /// to the (lazily-ensured, reused) production AppContainer profile, and stages archivePath
    /// into "in\" via hardlink-or-copy. The profile itself is created once, lazily, and reused
    /// for the lifetime of the install — never per-scope. The final move from "out\" to the
    /// user's chosen destination happens later, at Pakko's normal process identity, and is
    /// already cross-volume-safe (a per-file File.Move, not a directory rename) — so rooting the
    /// quarantine under %TEMP% instead of next to the destination costs at most an extra copy
    /// instead of a rename when they're on different volumes, never a correctness problem.
    /// </summary>
    public static Task<TarSandboxScope> CreateAsync(
        string archivePath, bool needsOutputDir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Checked once per scope (covers every RunAsync call made through it — the pre-scan and
        // the extraction both use the same scope) rather than once per tar.exe launch. This is
        // the correct choke point: SandboxedProcessLauncher itself is generic (it also launches
        // plain cmd.exe in its own unit tests), so it cannot assume "the target is always
        // tar.exe" the way this scope — which always launches the one hardcoded TarExecutablePath
        // constant — can. Cheap, defense-in-depth only; TOCTOU between this check and the actual
        // launch means it is not load-bearing against a real attacker (see TASKS.md's T-F52 entry).
        if (!TarSignatureVerifier.Verify(TarExecutablePath))
            throw new TarSignatureVerificationException(TarExecutablePath);

        try
        {
            var profile = new AppContainerProfile(AppContainerProfile.ProductionProfileName);
            profile.EnsureExists();
            SafeSidHandle sid = profile.GetSid();

            Directory.CreateDirectory(SandboxParentDirectory);
            QuarantineAcl.GrantTraverseOnly(SandboxParentDirectory, sid);

            string quarantineRoot = Path.Combine(SandboxParentDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(quarantineRoot);
            QuarantineAcl.GrantTraverseOnly(quarantineRoot, sid);

            string inDir = Path.Combine(quarantineRoot, "in");
            Directory.CreateDirectory(inDir);
            QuarantineAcl.GrantReadExecute(inDir, sid);

            string? outDir = null;
            if (needsOutputDir)
            {
                outDir = Path.Combine(quarantineRoot, "out");
                Directory.CreateDirectory(outDir);
                QuarantineAcl.GrantModify(outDir, sid);
            }

            string stagedArchivePath = Path.Combine(inDir, Path.GetFileName(archivePath));
            QuarantineStaging.StageArchive(archivePath, stagedArchivePath);
            // A hardlinked staged file shares its security descriptor with the ORIGINAL archive, not
            // the containing "in\" folder's — NTFS hard links are just an extra directory entry
            // pointing at the same file object, and that object's DACL doesn't change, so folder-level
            // inheritance never applies to it. Without this explicit per-file grant, a hardlinked
            // staged archive is unreadable to the AppContainer even though "in\" itself is correctly
            // ACL'd (found empirically — see DECISIONS.md's T-F52 entry). A copied file would already
            // inherit this from "in\" at creation time, but granting explicitly here is harmless and
            // correct for both cases.
            QuarantineAcl.GrantReadExecute(stagedArchivePath, sid);

            SecurityCapabilitiesAttributeList securityCapabilities = SecurityCapabilitiesAttributeList.Create(sid);

            return Task.FromResult(new TarSandboxScope(sid, securityCapabilities, quarantineRoot, stagedArchivePath, outDir));
        }
        catch (InvalidOperationException ex)
        {
            // AppContainer/ACL/attribute-list setup can fail at runtime (e.g. group policy
            // blocking profile creation) — fail closed as an ordinary per-archive error, never
            // an unhandled crash. Callers catch this the same way as TarSignatureVerificationException.
            throw new SandboxSetupException($"Sandbox setup failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs a single tar.exe invocation (pre-scan or extraction) inside this scope's
    /// AppContainer, under a fresh Job Object (ActiveProcessLimit = 1, RAM/CPU limits) created
    /// for just this one process launch.
    /// </summary>
    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        IReadOnlyList<string> tarArguments, CancellationToken cancellationToken)
    {
        SandboxJobObject job;
        try
        {
            job = SandboxJobObject.Create(RamLimitBytes, CpuTimeLimit);
        }
        catch (InvalidOperationException ex)
        {
            throw new SandboxSetupException($"Sandbox setup failed: {ex.Message}", ex);
        }

        using (job)
        {
            return await SandboxedProcessLauncher.RunAsync(
                TarExecutablePath, tarArguments, _securityCapabilities.AttributeList, job.Handle, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _securityCapabilities.Dispose();
        _sid.Dispose();
        // The AppContainer profile itself is never deleted here — it's created once, lazily,
        // and reused for the lifetime of the install (see DECISIONS.md's T-F52 follow-up entry).
        try { if (Directory.Exists(_quarantineRoot)) Directory.Delete(_quarantineRoot, recursive: true); } catch { }
    }
}

/// <summary>
/// Thrown by <see cref="TarSandboxScope.CreateAsync"/> when tar.exe's Authenticode signature
/// check fails — fail-closed: this is treated as an ordinary per-archive error by callers, never
/// a silent fallback to running tar.exe unsandboxed or unverified.
/// </summary>
internal sealed class TarSignatureVerificationException(string tarExecutablePath)
    : Exception($"'{tarExecutablePath}' failed Authenticode signature verification.");

/// <summary>
/// Thrown by <see cref="TarSandboxScope.CreateAsync"/>/<see cref="TarSandboxScope.RunAsync"/> when
/// AppContainer profile/ACL/attribute-list/Job-Object setup fails (e.g. a Win32 security API
/// blocked by group policy) — fail-closed: treated as an ordinary per-archive error by callers,
/// never a silent fallback to unsandboxed extraction.
/// </summary>
internal sealed class SandboxSetupException(string message, Exception innerException)
    : Exception(message, innerException);
