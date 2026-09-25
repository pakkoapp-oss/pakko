using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// The PROC_THREAD_ATTRIBUTE_LIST for one process launch: PROC_THREAD_ATTRIBUTE_SECURITY_
/// CAPABILITIES with an empty capability list when an AppContainer SID is given (no network, no
/// other capability — confirmed against a real "tar.exe --version" launch in T-F52's Phase 0, see
/// DECISIONS.md), and always PROC_THREAD_ATTRIBUTE_HANDLE_LIST naming the only handles the child
/// may inherit (T-F244 item 5). Built per launch, since the inherited handles differ each time.
/// </summary>
internal sealed class LaunchAttributeList : IDisposable
{
    // ProcThreadAttributeValue(ProcThreadAttributeSecurityCapabilities = 9, Thread = FALSE,
    // Input = TRUE, Additive = FALSE) = 9 | PROC_THREAD_ATTRIBUTE_INPUT(0x00020000) = 0x00020009.
    // Matches the documented constant used in Microsoft's own AppContainer sample code.
    private static readonly IntPtr ProcThreadAttributeSecurityCapabilities = (IntPtr)0x00020009;

    // ProcThreadAttributeValue(ProcThreadAttributeHandleList = 2, FALSE, TRUE, FALSE) = 0x00020002.
    private static readonly IntPtr ProcThreadAttributeHandleList = (IntPtr)0x00020002;

    private readonly SafeProcThreadAttributeListHandle _attributeList = new();

    // Both buffers are only pointed at by the attribute list, never copied into it — they must
    // outlive it, and are released in Dispose after it.
    private IntPtr _securityCapabilitiesBuffer;
    private IntPtr _handleListBuffer;

    private LaunchAttributeList() { }

    public SafeProcThreadAttributeListHandle AttributeList => _attributeList;

    /// <summary>
    /// Builds the list. The caller keeps <paramref name="appContainerSid"/> and every handle in
    /// <paramref name="inheritedHandles"/> alive (and pinned) until CreateProcessW returns — the
    /// list stores raw values only.
    /// </summary>
    public static LaunchAttributeList Create(IntPtr appContainerSid, IReadOnlyList<IntPtr> inheritedHandles)
    {
        int attributeCount = (appContainerSid != IntPtr.Zero ? 1 : 0) + (inheritedHandles.Count > 0 ? 1 : 0);

        IntPtr size = IntPtr.Zero;
        // First call is expected to "fail" (ERROR_INSUFFICIENT_BUFFER) — its only job is to
        // report the required buffer size via lpSize. No SafeProcThreadAttributeListHandle exists
        // yet to pass, so this uses the IntPtr.Zero-only size-probe overload (see NativeMethods).
        NativeMethods.InitializeProcThreadAttributeListSizeProbe(IntPtr.Zero, attributeCount, 0, ref size);

        var list = new LaunchAttributeList();
        try
        {
            list._attributeList.SetBuffer(Marshal.AllocHGlobal(size));
            if (!NativeMethods.InitializeProcThreadAttributeList(list._attributeList, attributeCount, 0, ref size))
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed (Win32 error {Marshal.GetLastWin32Error()}).");

            if (appContainerSid != IntPtr.Zero)
            {
                var securityCapabilities = new SECURITY_CAPABILITIES
                {
                    AppContainerSid = appContainerSid,
                    Capabilities = IntPtr.Zero,
                    CapabilityCount = 0,
                    Reserved = 0,
                };
                int structSize = Marshal.SizeOf<SECURITY_CAPABILITIES>();
                list._securityCapabilitiesBuffer = Marshal.AllocHGlobal(structSize);
                Marshal.StructureToPtr(securityCapabilities, list._securityCapabilitiesBuffer, fDeleteOld: false);
                Update(list._attributeList, ProcThreadAttributeSecurityCapabilities, list._securityCapabilitiesBuffer, structSize);
            }

            if (inheritedHandles.Count > 0)
            {
                IntPtr[] handles = [.. inheritedHandles];
                int bytes = IntPtr.Size * handles.Length;
                list._handleListBuffer = Marshal.AllocHGlobal(bytes);
                Marshal.Copy(handles, 0, list._handleListBuffer, handles.Length);
                Update(list._attributeList, ProcThreadAttributeHandleList, list._handleListBuffer, bytes);
            }

            return list;
        }
        catch
        {
            list.Dispose();
            throw;
        }
    }

    private static void Update(SafeProcThreadAttributeListHandle attributeList, IntPtr attribute, IntPtr value, int size)
    {
        if (!NativeMethods.UpdateProcThreadAttribute(attributeList, 0, attribute, value, size, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    public void Dispose()
    {
        // Attribute list (and its DeleteProcThreadAttributeList call) must go first — it may
        // still reference both buffers internally until torn down.
        _attributeList.Dispose();
        if (_securityCapabilitiesBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_securityCapabilitiesBuffer);
        if (_handleListBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_handleListBuffer);
        _securityCapabilitiesBuffer = IntPtr.Zero;
        _handleListBuffer = IntPtr.Zero;
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

        // Separate IntPtr-typed overload for the buffer-size probe call only (Create() above,
        // before any buffer exists to pass) — a SafeHandle-typed P/Invoke parameter throws
        // ArgumentNullException on null rather than marshaling it as a zero handle, so the
        // two-call size-then-allocate idiom needs this real IntPtr.Zero entry point.
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
