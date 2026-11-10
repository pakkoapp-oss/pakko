using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F470: decides once per process whether tar-family archives are read by the archive helper over
/// Windows' own <c>archiveint.dll</c> or by tar.exe. The first decision stands for the life of the
/// process, a failed or timed-out probe included, so a listing and the extraction that follows it
/// never read through different backends. Built once per process and shared by every service.
/// </summary>
public sealed partial class ArchiveintGate
{
    private const ushort MachineAmd64 = 0x8664;
    private const string ArchiveintPath = @"C:\Windows\System32\archiveint.dll"; // NOSONAR: S1075 — the system DLL the helper loads, by absolute path (CLAUDE.md)
    private const string HelperFileName = "Archiver.ArchiveHelper.exe";

    // As patient as the 7z writer probe (T-F450): a cold AppContainer's first launch must not fix
    // tar.exe for the whole process.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly GroupPolicyOptions _policy;
    private readonly string _helperPath;
    private readonly Func<ushort?> _nativeMachine;
    private readonly Func<string, bool> _verifyArchiveint;
    private readonly Func<string, CancellationToken, Task<ArchiveHelperOutput>> _probe;
    private readonly TimeSpan _probeTimeout;
    private readonly Lazy<Task<ArchiveintDecision>> _decision;

    /// <summary>The gate for this process: the helper beside Pakko's own binaries.</summary>
    public ArchiveintGate(GroupPolicyOptions policy)
        : this(policy, Path.Combine(AppContext.BaseDirectory, HelperFileName))
    {
    }

    // Test seams: the helper's path, the machine, the DLL's signature check, the probe and its patience.
    internal ArchiveintGate(
        GroupPolicyOptions policy, string helperPath, Func<ushort?>? nativeMachine = null, Func<string, bool>? verifyArchiveint = null,
        Func<string, CancellationToken, Task<ArchiveHelperOutput>>? probe = null, TimeSpan? probeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _helperPath = helperPath;
        _nativeMachine = nativeMachine ?? NativeMachineOfAnX64Process;
        _verifyArchiveint = verifyArchiveint ?? TarSignatureVerifier.Verify;
        _probe = probe ?? ProbeAsync;
        _probeTimeout = probeTimeout ?? ProbeTimeout;
        // T-F490: on the thread pool from the start; the checks before the probe's first await (the
        // signature check of archiveint.dll) would otherwise hold the first caller's thread, a UI thread.
        _decision = new Lazy<Task<ArchiveintDecision>>(() => Task.Run(DecideOnceAsync), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The decision, made on the first call; never faults.</summary>
    public Task<ArchiveintDecision> DecideAsync() => _decision.Value;

    // The decision runs on its own timeout, never a caller's token: a cancelled first operation
    // must not become the verdict for the whole process.
    internal async Task<HelperSetup?> HelperAsync(CancellationToken cancellationToken) =>
        (await _decision.Value.WaitAsync(cancellationToken).ConfigureAwait(false)).Helper;

    private async Task<ArchiveintDecision> DecideOnceAsync()
    {
        try
        {
            if (_policy.DisableTarExtraction)
                return ArchiveintDecision.ForTarExe(null, "Group Policy disables tar-family archives");
            if (!File.Exists(_helperPath))
                return ArchiveintDecision.ForTarExe(null, "no archive helper beside Pakko");
            // ARM64's archiveint.dll is not measured; an x64 process under emulation is not x64.
            if (_nativeMachine() != MachineAmd64)
                return ArchiveintDecision.ForTarExe(null, "not a native x64 process");
            if (!File.Exists(ArchiveintPath) || !_verifyArchiveint(ArchiveintPath))
                return ArchiveintDecision.ForTarExe(null, "archiveint.dll is missing or not signed by Microsoft");
            using var timeout = new CancellationTokenSource(_probeTimeout);
            ArchiveHelperOutput output = await _probe(_helperPath, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return Judge(output, _helperPath);
        }
        catch (OperationCanceledException)
        {
            return ArchiveintDecision.ForTarExe(null, "the probe timed out");
        }
        catch (Exception ex) // The decision falls back to tar.exe on any failure of the probe and says why.
        {
            return ArchiveintDecision.ForTarExe(null, "the probe failed: " + ex.Message);
        }
    }

    /// <summary>The probe's verdict: a finished run, every export, libarchive 3.5 to 3.8 (measured, T-F468).</summary>
    internal static ArchiveintDecision Judge(ArchiveHelperOutput output, string helperPath)
    {
        string? version = output.Version;
        if (!output.Done)
            return ArchiveintDecision.ForTarExe(version, "the probe did not finish");
        if (output.MissingExports.Count > 0)
            return ArchiveintDecision.ForTarExe(version, "archiveint.dll lacks " + string.Join(", ", output.MissingExports));
        if (version is null || !MeasuredVersion().IsMatch(version))
            return ArchiveintDecision.ForTarExe(version, "a libarchive version not measured");
        return ArchiveintDecision.ForHelper(version, new HelperSetup(helperPath, output.Capabilities));
    }

    private static async Task<ArchiveHelperOutput> ProbeAsync(string helperPath, CancellationToken cancellationToken)
    {
        // The scope reads an archive on the helper's stdin; the probe reads none, so an empty tar.
        string archive = Path.Combine(Path.GetTempPath(), "pakko-probe-" + Guid.NewGuid().ToString("N") + ".tar");
        await File.WriteAllBytesAsync(archive, new byte[1024], cancellationToken).ConfigureAwait(false);
        try
        {
            using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archive, needsOutputDir: false, cancellationToken, helperSource: helperPath)
                .ConfigureAwait(false);
            var output = new ArchiveHelperOutput(ArchiveHelperMode.Probe);
            await scope.RunHelperAsync(ArchiveHelperMode.Probe, [], null, output, cancellationToken).ConfigureAwait(false);
            return output;
        }
        finally
        {
            try
            {
                File.Delete(archive);
            }
            catch (IOException)
            {
                /* best-effort: an empty file in the temp folder */
            }
        }
    }

    // Null for a process that is not x64 itself, so one seam stands for both conditions and a test
    // that fakes the machine decides the same on an ARM64 runner (the canary).
    private static ushort? NativeMachineOfAnX64Process() =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64 && IsWow64Process2(-1, out _, out ushort native) ? native : null;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWow64Process2(nint process, out ushort processMachine, out ushort nativeMachine);

    [GeneratedRegex(@"^libarchive 3\.[5-8]\.[0-9]+( |$)", RegexOptions.CultureInvariant)]
    private static partial Regex MeasuredVersion();
}
