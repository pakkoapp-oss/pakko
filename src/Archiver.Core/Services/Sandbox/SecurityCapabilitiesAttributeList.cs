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
        // report the required buffer size via lpSize. No SafeProcThreadAttributeListHandle exists
        // yet to pass, so this uses the IntPtr.Zero-only size-probe overload (see NativeMethods).
        NativeMethods.InitializeProcThreadAttributeListSizeProbe(IntPtr.Zero, 1, 0, ref size);

        var attributeListHandle = new SafeProcThreadAttributeListHandle();
        attributeListHandle.SetBuffer(Marshal.AllocHGlobal(size));

        // appContainerSid's underlying memory stays reachable well past this method returns —
        // TarSandboxScope (the only caller) keeps its own SafeSidHandle rooted as a field for the
        // scope's whole lifetime, which is what actually protects the raw pointer value written
        // into the unmanaged SECURITY_CAPABILITIES buffer below until the real CreateProcessW
        // dereferences it.
        //
        // T-F138: InitializeProcThreadAttributeList takes attributeListHandle directly (SafeHandle-
        // typed P/Invoke parameter — the CLR marshaller pins/releases it automatically, no manual
        // DangerousGetHandle()). AppContainerSid below is a genuine exception, not an oversight:
        // it's a PSID written into a plain unmanaged struct field (SECURITY_CAPABILITIES), not a
        // P/Invoke parameter the marshaller can intercept, and a PSID isn't a kernel handle at all
        // (released via FreeSid, not CloseHandle) — SafeHandle's pin/release contract doesn't apply
        // to it. DangerousAddRef/Release stays as the real (if manual) protection for this one read.
        bool sidRefAdded = false;
        SECURITY_CAPABILITIES securityCapabilities;
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeListHandle, 1, 0, ref size))
            {
                int error = Marshal.GetLastWin32Error();
                attributeListHandle.Dispose();
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed (Win32 error {error}).");
            }

            appContainerSid.DangerousAddRef(ref sidRefAdded);
            securityCapabilities = new SECURITY_CAPABILITIES
            {
                AppContainerSid = appContainerSid.DangerousGetHandle(), // NOSONAR: S3869 — PSID struct field, not a P/Invoke parameter the marshaller can intercept; DangerousAddRef/Release above pins it for this read (see comment above)
                Capabilities = IntPtr.Zero,
                CapabilityCount = 0,
                Reserved = 0,
            };
        }
        finally
        {
            if (sidRefAdded)
                appContainerSid.DangerousRelease();
        }

        int structSize = Marshal.SizeOf<SECURITY_CAPABILITIES>();
        IntPtr securityCapabilitiesBuffer = Marshal.AllocHGlobal(structSize);
        Marshal.StructureToPtr(securityCapabilities, securityCapabilitiesBuffer, fDeleteOld: false);

        // UpdateProcThreadAttribute only stores a pointer to this buffer — it must stay alive
        // (not be freed) until the attribute list itself is torn down, which is why both buffers
        // are released together in Dispose(), not individually right after this call returns.
        bool updated = NativeMethods.UpdateProcThreadAttribute(
            attributeListHandle,
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
    private struct SECURITY_CAPABILITIES // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
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
            SafeProcThreadAttributeListHandle lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        // Separate IntPtr-typed overload for the buffer-size probe call only (Create() below,
        // before any SafeProcThreadAttributeListHandle exists to pass) — a SafeHandle-typed
        // P/Invoke parameter throws ArgumentNullException on null rather than marshaling it as a
        // zero handle, so the two-call size-then-allocate idiom needs this real IntPtr.Zero entry
        // point.
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "InitializeProcThreadAttributeList")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeListSizeProbe(
            IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(
            SafeProcThreadAttributeListHandle lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);
    }
}
