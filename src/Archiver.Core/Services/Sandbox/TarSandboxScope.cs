using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Disposable orchestration tying the AppContainer profile, quarantine ACLs and a Job Object
/// into one scope per archive operation. <see cref="ListAsync"/> and <see cref="ExtractAsync"/>
/// are the only ways a sandboxed tar.exe runs — T-F49's whole-archive pre-scan and, only if that
/// passes, the extraction itself, within the same scope, and ListEntriesAsync with
/// <c>needsOutputDir: false</c> (no "out\" folder or ACE at all, since listing never writes).
/// T-F233: tar.exe reads the archive only as an inherited stdin handle ("-f -") that Pakko
/// opened itself. The AppContainer gets no path to the user's file and no ACE on it — the
/// hardlink staging this replaced rewrote the original's DACL — and the archive stays open
/// read-only, sharing read only, for the whole scope, so the bytes the pre-scan checked are the
/// bytes extraction reads.
/// </summary>
internal sealed class TarSandboxScope : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe"; // NOSONAR: S1075 — CLAUDE.md's Hard Constraints mandate this exact absolute path, never PATH-resolved (PATH-hijack resistance); moving it to config would reopen that risk
    private const long RamLimitBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan CpuTimeLimit = TimeSpan.FromMinutes(5);

    // tar.exe runs with the quarantine root as its current directory and extracts into this
    // relative folder — no user-profile path appears in its command line.
    private const string OutputFolderName = "out";

    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;

    // "tar:" scopes the option to the tar reader; 7z/rar/zip ignore it (confirmed 2026-09-25).
    private const string Utf8HeaderOption = "tar:hdrcharset=UTF-8";
    private const string NotUtf8HeaderMessage = "Pathname can't be converted from UTF-8";

    private readonly SafeSidHandle _sid;
    private readonly string _quarantineRoot;
    private readonly FileStream _archive;

    // Null until the first listing decides it (see ListAsync).
    private bool? _utf8Headers;

    /// <summary>Null when this scope was created with needsOutputDir: false (listing only).</summary>
    public string? OutputDirectory { get; }

    /// <summary>The operation-scoped quarantine directory (contains "out\" if present) — deleted whole by Dispose().</summary>
    public string QuarantineRoot => _quarantineRoot;

    private TarSandboxScope(SafeSidHandle sid, string quarantineRoot, FileStream archive, string? outputDirectory)
    {
        _sid = sid;
        _quarantineRoot = quarantineRoot;
        _archive = archive;
        OutputDirectory = outputDirectory;
    }

    // Rooted under %TEMP%, not "same disk as destination" as TASKS.md's original flow described —
    // an AppContainer token has no bypass-traverse-checking privilege, so FILE_TRAVERSE is
    // enforced on every ancestor directory down to "out\". A fresh directory created as a
    // sibling of the user's arbitrary destination folder (e.g. inside Desktop/Documents/a network
    // share) sits under an ancestor chain Pakko doesn't control and can't grant traverse on
    // without touching folders it doesn't own. %TEMP% itself is already AppContainer-traversable
    // (confirmed empirically — QuarantineAclTests grants traverse only on its own quarantine root,
    // one level below %TEMP%, and that already succeeds), so rooting here needs traverse grants
    // on only the two levels Pakko itself creates. See DECISIONS.md's T-F52 entry for the full
    // empirical trace (a nested-one-level-deeper test failed until this was found).
    private static readonly string SandboxParentDirectory = Path.Combine(Path.GetTempPath(), "PakkoTarSandbox");

    /// <summary>
    /// Opens the archive (read-only, sharing read only), then sets up a fresh operation-scoped
    /// quarantine directory beneath <see cref="SandboxParentDirectory"/> — with an "out\" folder
    /// if needed — ACL'd to the (lazily-ensured, reused) production AppContainer profile. The
    /// profile itself is created once and reused for the lifetime of the install. The final move
    /// from "out\" to the user's destination happens later, at Pakko's normal process identity.
    /// </summary>
    public static Task<TarSandboxScope> CreateAsync(
        string archivePath, bool needsOutputDir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Checked once per scope (covers every tar.exe run made through it — the pre-scan and
        // the extraction both use the same scope) rather than once per tar.exe launch. This is
        // the correct choke point: SandboxedProcessLauncher itself is generic (it also launches
        // plain cmd.exe in its own unit tests), so it cannot assume "the target is always
        // tar.exe" the way this scope — which always launches the one hardcoded TarExecutablePath
        // constant — can. Cheap, defense-in-depth only; TOCTOU between this check and the actual
        // launch means it is not load-bearing against a real attacker (see TASKS.md's T-F52 entry).
        if (!TarSignatureVerifier.Verify(TarExecutablePath))
            throw new TarSignatureVerificationException(TarExecutablePath);

        // Opened first: nothing is created on disk for an archive that cannot be opened.
        FileStream archive = OpenArchive(archivePath);

        // Owned by this method until the scope object exists (the very last statement) — any
        // failure before that must release them itself, since no Dispose() will ever run.
        SafeSidHandle? sid = null;
        string? quarantineRoot = null;
        try
        {
            var profile = new AppContainerProfile(AppContainerProfile.ProductionProfileName);
            profile.EnsureExists();
            sid = profile.GetSid();

            Directory.CreateDirectory(SandboxParentDirectory);
            QuarantineAcl.EnsureSharedParentTraverse(SandboxParentDirectory, sid);

            quarantineRoot = Path.Combine(SandboxParentDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(quarantineRoot);
            QuarantineAcl.GrantTraverseListReadAttributes(quarantineRoot, sid);

            string? outDir = null;
            if (needsOutputDir)
            {
                outDir = Path.Combine(quarantineRoot, OutputFolderName);
                Directory.CreateDirectory(outDir);
                QuarantineAcl.GrantModify(outDir, sid);
            }

            return Task.FromResult(new TarSandboxScope(sid, quarantineRoot, archive, outDir));
        }
        catch (Exception ex)
        {
            // Found 2026-09-24: a setup failure used to leave a half-built quarantine behind.
            archive.Dispose();
            sid?.Dispose();
            if (quarantineRoot is not null)
                try { Directory.Delete(quarantineRoot, recursive: true); } catch { /* best-effort cleanup */ }

            // AppContainer/ACL/attribute-list setup can fail at runtime (e.g. group policy
            // blocking profile creation) — fail closed as an ordinary per-archive error, never
            // an unhandled crash. Callers catch this the same way as TarSignatureVerificationException.
            if (ex is InvalidOperationException)
                throw new SandboxSetupException($"Sandbox setup failed: {ex.Message}", ex);
            throw;
        }
    }

    // Read-only, sharing read only: no writer can change the archive and no one can rename or
    // delete it while the scope holds it. A file another program is still writing (a download
    // in progress) cannot be opened this way — refused rather than read while it changes.
    private static FileStream OpenArchive(string archivePath)
    {
        try
        {
            return new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException ex) when (ex.HResult == ErrorSharingViolation)
        {
            throw new IOException($"The archive is in use by another program: {archivePath}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Cannot open the archive: {ex.Message}", ex);
        }
    }

    /// <summary>Lists the archive ("-t", or "-tv" when <paramref name="verbose"/>).</summary>
    public async Task<(int ExitCode, string StdOut, string StdErr)> ListAsync(bool verbose, CancellationToken cancellationToken)
    {
        string mode = verbose ? "-tv" : "-t";
        if (_utf8Headers is not null)
            return await RunAsync(mode, [], cancellationToken).ConfigureAwait(false);

        // T-F204: tar headers carry no charset; libarchive reads them as the OEM code page, so a
        // GNU tar or 7-Zip archive with UTF-8 names was extracted under mojibake names. 7-Zip's
        // rule: UTF-8 when every name is valid UTF-8, else the OEM page. libarchive reports
        // invalid UTF-8 under hdrcharset=UTF-8 with this exact message (a valid UTF-8 name it
        // merely cannot show gives only "unreadable filename" — confirmed 2026-09-25). Decided
        // once, on the first listing, and used for every later run of this scope.
        _utf8Headers = true;
        var result = await RunAsync(mode, [], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 || !result.StdErr.Contains(NotUtf8HeaderMessage, StringComparison.Ordinal))
            return result;

        _utf8Headers = false;
        return await RunAsync(mode, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Extracts the archive, or only <paramref name="members"/>, into <see cref="OutputDirectory"/>.</summary>
    public async Task<(int ExitCode, string StdOut, string StdErr)> ExtractAsync(
        IReadOnlyList<string>? members, CancellationToken cancellationToken)
    {
        if (OutputDirectory is null)
            throw new InvalidOperationException("This scope was created without an output folder.");
        if (_utf8Headers is null)
            await ListAsync(verbose: false, cancellationToken).ConfigureAwait(false);
        return await RunAsync("-x", ["-C", OutputFolderName, .. members ?? []], cancellationToken).ConfigureAwait(false);
    }

    // One tar.exe run inside this scope's AppContainer, under a fresh Job Object (ActiveProcessLimit
    // = 1, RAM/CPU limits), reading the archive from its own handle with its own file position.
    private async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string mode, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string[] headerCharset = _utf8Headers == true ? ["--options", Utf8HeaderOption] : [];
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
        using (SafeFileHandle stdIn = ReopenArchive())
        {
            var (exitCode, stdOut, stdErr) = await SandboxedProcessLauncher.RunAsync(
                TarExecutablePath,
                [mode, "-f", "-", .. headerCharset, .. arguments],
                new ProcessLaunchOptions(AppContainerSid: _sid, Job: job.Handle, StdIn: stdIn, WorkingDirectory: _quarantineRoot,
                    OutputEncoding: TarOutputEncoding.Current),
                cancellationToken)
                .ConfigureAwait(false);

            // T-F239: say which sandbox limit stopped tar.exe — its own words ("Cannot allocate
            // memory", or nothing at all for a CPU-time kill) blame the machine, not the sandbox.
            if (exitCode != 0)
                stdErr = DescribeLimitHit(job.ReadLimitHit()) + stdErr;
            return (exitCode, stdOut, stdErr);
        }
    }

    internal static string DescribeLimitHit(SandboxJobObject.LimitHit hit) => hit switch
    {
        SandboxJobObject.LimitHit.Memory =>
            $"The archive needs more memory than Pakko's sandbox allows tar.exe ({RamLimitBytes / (1024 * 1024)} MB). ",
        SandboxJobObject.LimitHit.CpuTime =>
            $"tar.exe used more processor time than Pakko's sandbox allows ({CpuTimeLimit.TotalMinutes:0} minutes). ",
        _ => string.Empty,
    };

    // A second handle to the same open file (not a new lookup by path), starting at offset 0 and
    // synchronous — the C runtime's stdin reads in tar.exe expect a non-overlapped handle.
    private SafeFileHandle ReopenArchive()
    {
        SafeFileHandle handle = NativeMethods.ReOpenFile(_archive.SafeFileHandle, GenericRead, FileShareRead, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"Cannot reopen the archive (Win32 error {error}).");
        }
        return handle;
    }

    public void Dispose()
    {
        _archive.Dispose();
        _sid.Dispose();
        // The AppContainer profile itself is never deleted here — it's created once, lazily,
        // and reused for the lifetime of the install (see DECISIONS.md's T-F52 follow-up entry).
        try { if (Directory.Exists(_quarantineRoot)) Directory.Delete(_quarantineRoot, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle ReOpenFile(
            SafeFileHandle hOriginalFile, uint dwDesiredAccess, uint dwShareMode, uint dwFlagsAndAttributes);
    }
}

/// <summary>
/// Thrown by <see cref="TarSandboxScope.CreateAsync"/> when tar.exe's Authenticode signature
/// check fails — fail-closed: this is treated as an ordinary per-archive error by callers, never
/// a silent fallback to running tar.exe unsandboxed or unverified.
/// </summary>
internal sealed class TarSignatureVerificationException(string tarExecutablePath) // NOSONAR: S3871 — deliberately internal, never escapes Archiver.Core's public surface (always caught and converted to ArchiveError, per this project's "services never throw to callers" rule); public would be pure API-surface bloat
    : Exception($"'{tarExecutablePath}' failed Authenticode signature verification.");

/// <summary>
/// Thrown by <see cref="TarSandboxScope.CreateAsync"/>/a scope's tar.exe run when
/// AppContainer profile/ACL/attribute-list/Job-Object setup fails (e.g. a Win32 security API
/// blocked by group policy) — fail-closed: treated as an ordinary per-archive error by callers,
/// never a silent fallback to unsandboxed extraction.
/// </summary>
internal sealed class SandboxSetupException(string message, Exception innerException) // NOSONAR: S3871 — deliberately internal, never escapes Archiver.Core's public surface (see comment above)
    : Exception(message, innerException);
