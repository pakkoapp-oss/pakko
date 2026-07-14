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

    /// <summary>
    /// Creates the profile if it doesn't already exist. Safe to call on every operation —
    /// ERROR_ALREADY_EXISTS is treated as success, not an error.
    /// </summary>
    public void EnsureExists()
    {
        int hr = NativeMethods.CreateAppContainerProfile(
            _profileName,
            _profileName,
            _profileName,
            pCapabilities: IntPtr.Zero,
            dwCapabilityCount: 0,
            out IntPtr sid);

        if (hr == HResultAlreadyExists)
            return;

        if (hr < 0)
            throw new InvalidOperationException($"CreateAppContainerProfile failed (HRESULT 0x{hr:X8}).");

        // Profile was just created — this call still yields a usable SID, but callers always
        // re-derive it via GetSid() instead (deterministic, no cached live handle to manage).
        NativeMethods.FreeSid(sid);
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
    }
}
