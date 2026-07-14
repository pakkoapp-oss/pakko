using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Builds a one-attribute PROC_THREAD_ATTRIBUTE_LIST carrying PROC_THREAD_ATTRIBUTE_SECURITY_
/// CAPABILITIES with an empty capability list (no internetClient/internetClientServer, no other
/// capability) — this is what actually confines a launched process to the AppContainer SID with
/// no network access. Confirmed working against a real "tar.exe --version" launch in Phase 0
/// (see DECISIONS.md's T-F52 entry); this class formalizes that spike into production code.
/// </summary>
internal sealed class SecurityCapabilitiesAttributeList : IDisposable
{
    // ProcThreadAttributeValue(ProcThreadAttributeSecurityCapabilities = 9, Thread = FALSE,
    // Input = TRUE, Additive = FALSE) = 9 | PROC_THREAD_ATTRIBUTE_INPUT(0x00020000) = 0x00020009.
    // Matches the documented constant used in Microsoft's own AppContainer sample code.
    private static readonly IntPtr ProcThreadAttributeSecurityCapabilities = (IntPtr)0x00020009;

    private readonly SafeProcThreadAttributeListHandle _attributeList;
    private readonly IntPtr _securityCapabilitiesBuffer;

    private SecurityCapabilitiesAttributeList(SafeProcThreadAttributeListHandle attributeList, IntPtr securityCapabilitiesBuffer)
    {
        _attributeList = attributeList;
        _securityCapabilitiesBuffer = securityCapabilitiesBuffer;
    }

    public SafeProcThreadAttributeListHandle AttributeList => _attributeList;

    public static SecurityCapabilitiesAttributeList Create(SafeSidHandle appContainerSid)
    {
        IntPtr size = IntPtr.Zero;
        // First call is expected to "fail" (ERROR_INSUFFICIENT_BUFFER) — its only job is to
        // report the required buffer size via lpSize.
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);

        var attributeListHandle = new SafeProcThreadAttributeListHandle();
        attributeListHandle.SetBuffer(Marshal.AllocHGlobal(size));

        if (!NativeMethods.InitializeProcThreadAttributeList(attributeListHandle.DangerousGetHandle(), 1, 0, ref size))
        {
            int error = Marshal.GetLastWin32Error();
            attributeListHandle.Dispose();
            throw new InvalidOperationException($"InitializeProcThreadAttributeList failed (Win32 error {error}).");
        }

        var securityCapabilities = new SECURITY_CAPABILITIES
        {
            AppContainerSid = appContainerSid.DangerousGetHandle(),
            Capabilities = IntPtr.Zero,
            CapabilityCount = 0,
            Reserved = 0,
        };

        int structSize = Marshal.SizeOf<SECURITY_CAPABILITIES>();
        IntPtr securityCapabilitiesBuffer = Marshal.AllocHGlobal(structSize);
        Marshal.StructureToPtr(securityCapabilities, securityCapabilitiesBuffer, fDeleteOld: false);

        // UpdateProcThreadAttribute only stores a pointer to this buffer — it must stay alive
        // (not be freed) until the attribute list itself is torn down, which is why both buffers
        // are released together in Dispose(), not individually right after this call returns.
        bool updated = NativeMethods.UpdateProcThreadAttribute(
            attributeListHandle.DangerousGetHandle(),
            dwFlags: 0,
            ProcThreadAttributeSecurityCapabilities,
            securityCapabilitiesBuffer,
            (IntPtr)structSize,
            IntPtr.Zero,
            IntPtr.Zero);

        if (!updated)
        {
            int error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(securityCapabilitiesBuffer);
            attributeListHandle.Dispose();
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed (Win32 error {error}).");
        }

        return new SecurityCapabilitiesAttributeList(attributeListHandle, securityCapabilitiesBuffer);
    }

    public void Dispose()
    {
        // Attribute list (and its DeleteProcThreadAttributeList call) must go first — it may
        // still reference the SECURITY_CAPABILITIES buffer internally until torn down.
        _attributeList.Dispose();
        Marshal.FreeHGlobal(_securityCapabilitiesBuffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);
    }
}
