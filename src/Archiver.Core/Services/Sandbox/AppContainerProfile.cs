using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Owns the lifecycle of a single named AppContainer profile. Production code always uses
/// <see cref="ProductionProfileName"/> and only ever calls <see cref="EnsureExists"/> — the
/// profile is created once, lazily, on first use and reused for the lifetime of the install
/// (see DECISIONS.md's T-F52 follow-up entry). <see cref="Delete"/> exists only for test teardown
/// of a throwaway test-only profile name; it must never be called against
/// <see cref="ProductionProfileName"/>.
/// </summary>
internal sealed class AppContainerProfile
{
    // Fixed, safe-to-share identity for every sandboxed tar.exe launch. Not per-operation —
    // see the Flow section of TASKS.md's T-F52 entry for why a per-operation profile would be
    // both slower (registry churn) and unsafe under T-F12's parallel SeparateArchives mode.
    public const string ProductionProfileName = "Pakko.TarSandbox";

    private const int HResultAlreadyExists = unchecked((int)0x800700B7); // HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)

    private readonly string _profileName;

    public AppContainerProfile(string profileName) => _profileName = profileName;

    // Where the OS records a profile (one key per AppContainer SID, confirmed 2026-09-25).
    private const string MappingsKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\";
    private static readonly object CreateLock = new();

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

    private bool IsRegisteredOnWindows() => OperatingSystem.IsWindows() && IsRegistered();

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
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(MappingsKey + sidText);
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

    private static class NativeMethods
    {
        [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int CreateAppContainerProfile(
            string pszAppContainerName,
            string pszDisplayName,
            string pszDescription,
            IntPtr pCapabilities,
            uint dwCapabilityCount,
            out IntPtr ppSidAppContainerSid);

        [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int DeriveAppContainerSidFromAppContainerName(
            string pszAppContainerName,
            out IntPtr ppsidAppContainerSid);

        [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int DeleteAppContainerProfile(string pszAppContainerName);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern IntPtr FreeSid(IntPtr pSid);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ConvertSidToStringSidW(SafeSidHandle sid, out IntPtr stringSid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);
    }
}
