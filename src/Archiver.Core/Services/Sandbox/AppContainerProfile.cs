using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Owns the lifecycle of a single named AppContainer profile. T-F523: every sandbox scope has a
/// profile of its own (<see cref="Create"/> when the scope starts, <see cref="TryDelete"/> when it
/// ends, <see cref="SweepStale"/> for one a killed run left): processes in one AppContainer can open
/// each other with every right, so one operation's compromised reader could read or write another's
/// (measured, docs/DECISIONS.md). <see cref="EnsureExists"/> and <see cref="Delete"/> serve the tests'
/// own throwaway profiles.
/// </summary>
internal sealed partial class AppContainerProfile
{
    // The one profile every operation shared from v1.4 to v1.10 (T-F52). No longer used (T-F523);
    // left registered, since an older Pakko still running may use it.
    public const string ProductionProfileName = "Pakko.TarSandbox";

    private const int HResultAlreadyExists = unchecked((int)0x800700B7); // HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)

    private readonly string _profileName;

    public AppContainerProfile(string profileName) => _profileName = profileName;

    // Where the OS records a profile (one key per AppContainer SID, confirmed 2026-09-25).
    private const string MappingsKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\";
    private static readonly Lock CreateLock = new();

    /// <summary>
    /// Creates the profile if it doesn't already exist. Safe to call on every operation.
    /// T-F244 item 1: CreateAppContainerProfile on an existing profile rewrites its registry keys,
    /// and concurrent calls failed with 0x800703FA/0x8000FFFF — two tar operations at once (two
    /// processes, or two test projects) got a spurious "Sandbox setup failed". An existing
    /// profile is now detected without calling it; creation is serialized in-process, and a
    /// failed creation counts as success if another process registered the profile meanwhile.
    /// </summary>
    public void EnsureExists()
    {
        if (IsRegisteredOnWindows())
            return;

        lock (CreateLock)
        {
            int hr = NativeMethods.CreateAppContainerProfile(
                _profileName,
                _profileName,
                _profileName,
                pCapabilities: IntPtr.Zero,
                dwCapabilityCount: 0,
                out IntPtr sid);

            if (hr >= 0)
            {
                // Callers always re-derive the SID via GetSid() (deterministic, no cached live handle).
                NativeMethods.FreeSid(sid);
                return;
            }

            if (hr != HResultAlreadyExists && !IsRegisteredOnWindows())
                throw new InvalidOperationException($"CreateAppContainerProfile failed (HRESULT 0x{hr:X8}).");
        }
    }

    /// <summary>
    /// T-F523: registers this profile, which must be new. A name that exists already throws, never
    /// counts as success: two operations must never share one sandbox. Serialized in-process (T-F244).
    /// </summary>
    public void Create()
    {
        lock (CreateLock)
        {
            int hr = NativeMethods.CreateAppContainerProfile(
                _profileName, _profileName, _profileName, pCapabilities: IntPtr.Zero, dwCapabilityCount: 0, out IntPtr sid);
            if (hr < 0)
                throw new InvalidOperationException($"CreateAppContainerProfile failed (HRESULT 0x{hr:X8}).");
            NativeMethods.FreeSid(sid);
        }
    }

    /// <summary>T-F523: removes this profile; false when Windows refused (a sweep removes it later).</summary>
    public bool TryDelete() => NativeMethods.DeleteAppContainerProfile(_profileName) >= 0;

    /// <summary>
    /// T-F523: removes the profiles named <paramref name="prefix"/> + an owner tag whose owner
    /// (<see cref="Archiver.Core.IO.TempOwner"/>'s "m&lt;machine&gt;-&lt;pid&gt;-&lt;ticks&gt;", then
    /// "-" and 8 hex) <paramref name="isOwnerRunning"/> says has ended: a run killed before its scope
    /// ended left them. A name of any other shape is never touched. Best-effort.
    /// </summary>
    public static void SweepStale(string prefix, Func<string, bool> isOwnerRunning)
    {
        if (OperatingSystem.IsWindows())
            SweepStaleOnWindows(prefix, isOwnerRunning);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SweepStaleOnWindows(string prefix, Func<string, bool> isOwnerRunning)
    {
        try
        {
            using RegistryKey? mappings = Registry.CurrentUser.OpenSubKey(MappingsKey.TrimEnd('\\'));
            if (mappings is null)
                return;
            foreach (string sidText in mappings.GetSubKeyNames())
            {
                string? moniker;
                using (RegistryKey? key = mappings.OpenSubKey(sidText))
                    moniker = key?.GetValue("Moniker") as string;
                if (moniker is not null && moniker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && OwnedName().IsMatch(moniker[prefix.Length..]) && !isOwnerRunning(moniker[prefix.Length..^9]))
                    _ = NativeMethods.DeleteAppContainerProfile(moniker);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // best-effort: the next scope sweeps again
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^m[0-9a-f]{8}-[0-9]+-[0-9]+-[0-9a-f]{8}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex OwnedName();

    internal bool IsRegisteredOnWindows() => OperatingSystem.IsWindows() && IsRegistered();

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private bool IsRegistered()
    {
        try
        {
            using SafeSidHandle sid = GetSid();
            if (!NativeMethods.ConvertSidToStringSidW(sid, out IntPtr text))
                return false;
            string sidText;
            try { sidText = Marshal.PtrToStringUni(text) ?? string.Empty; }
            finally { NativeMethods.LocalFree(text); }
            using RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(MappingsKey + sidText);
            return key is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false; // unknown — fall back to creating, which treats "already exists" as success
        }
    }

    /// <summary>
    /// Deterministically re-derives the profile's SID. Never caches a live handle — the SID is
    /// stable for a given profile name, so re-deriving per call costs nothing and avoids a
    /// lifetime/ownership question under concurrent operations.
    /// </summary>
    public SafeSidHandle GetSid()
    {
        int hr = NativeMethods.DeriveAppContainerSidFromAppContainerName(_profileName, out IntPtr sid);
        if (hr < 0)
            throw new InvalidOperationException($"DeriveAppContainerSidFromAppContainerName failed (HRESULT 0x{hr:X8}).");

        var handle = new SafeSidHandle();
        handle.Attach(sid);
        return handle;
    }

    /// <summary>
    /// Deletes this profile. Test-only — production code never deletes
    /// <see cref="ProductionProfileName"/>; only a test's own distinct throwaway profile name
    /// should ever be passed to a AppContainerProfile instance whose Delete() is called.
    /// </summary>
    public void Delete()
    {
        int hr = NativeMethods.DeleteAppContainerProfile(_profileName);
        if (hr < 0)
            throw new InvalidOperationException($"DeleteAppContainerProfile failed (HRESULT 0x{hr:X8}).");
    }

    private static partial class NativeMethods
    {
        [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = false)]
        public static partial int CreateAppContainerProfile(
            string pszAppContainerName,
            string pszDisplayName,
            string pszDescription,
            IntPtr pCapabilities,
            uint dwCapabilityCount,
            out IntPtr ppSidAppContainerSid);

        [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = false)]
        public static partial int DeriveAppContainerSidFromAppContainerName(
            string pszAppContainerName,
            out IntPtr ppsidAppContainerSid);

        [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = false)]
        public static partial int DeleteAppContainerProfile(string pszAppContainerName);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        public static partial IntPtr FreeSid(IntPtr pSid);

        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ConvertSidToStringSidW(SafeSidHandle sid, out IntPtr stringSid);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr LocalFree(IntPtr hMem);
    }
}
