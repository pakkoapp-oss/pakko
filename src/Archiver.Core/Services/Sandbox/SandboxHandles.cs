using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Wraps a PSID returned by CreateAppContainerProfile/DeriveAppContainerSidFromAppContainerName.
/// Released via FreeSid, not LocalFree/CloseHandle.
/// </summary>
internal sealed class SafeSidHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeSidHandle() : base(ownsHandle: true) { }

    // The SID arrives through an [out] IntPtr parameter (CreateAppContainerProfile /
    // DeriveAppContainerSidFromAppContainerName both yield PSID this way, not as a P/Invoke
    // return value the marshaler could adopt automatically), so callers attach it after the fact.
    public void Attach(IntPtr sid) => SetHandle(sid);

    protected override bool ReleaseHandle()
    {
        Interop.FreeSid(handle);
        return true;
    }
}

/// <summary>
/// Wraps a Job Object handle (CreateJobObjectW). Released via CloseHandle.
/// </summary>
internal sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobObjectHandle() : base(ownsHandle: true) { }

    public SafeJobObjectHandle(IntPtr existingHandle) : base(ownsHandle: true)
        => SetHandle(existingHandle);

    protected override bool ReleaseHandle() => Interop.CloseHandle(handle);
}

/// <summary>
/// Wraps a process or thread handle from PROCESS_INFORMATION (CreateProcessW). Released via
/// CloseHandle. Shared by both hProcess and hThread — both are plain kernel object handles with
/// identical lifetime rules, so one type covers both rather than two near-duplicates.
/// </summary>
internal sealed class SafeProcessOrThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeProcessOrThreadHandle() : base(ownsHandle: true) { }

    // Some callers (e.g. wrapping PROCESS_INFORMATION fields after CreateProcessW already
    // succeeded) need to attach an already-open handle rather than have P/Invoke marshal it in.
    public SafeProcessOrThreadHandle(IntPtr existingHandle) : base(ownsHandle: true)
        => SetHandle(existingHandle);

    protected override bool ReleaseHandle() => Interop.CloseHandle(handle);
}

/// <summary>
/// Wraps the native buffer behind an LPPROC_THREAD_ATTRIBUTE_LIST. Unlike the other three handles
/// here, this is not a kernel object — it's a heap buffer allocated with Marshal.AllocHGlobal.
/// ReleaseHandle calls DeleteProcThreadAttributeList (required by the API even though the memory
/// itself is separately freed) before releasing the buffer.
/// </summary>
internal sealed class SafeProcThreadAttributeListHandle : SafeHandle
{
    public SafeProcThreadAttributeListHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        Interop.DeleteProcThreadAttributeList(handle);
        Marshal.FreeHGlobal(handle);
        return true;
    }

    public void SetBuffer(IntPtr buffer) => SetHandle(buffer);
}

/// <summary>
/// The small set of raw P/Invoke declarations these four handle types release themselves through.
/// Kept private to this file — every other Sandbox/ class releases handles via Dispose(), never
/// by calling these directly, so there is no shared "NativeMethods god-class" here, just the
/// release primitives these SafeHandle subclasses need.
/// </summary>
file static class Interop
{
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern IntPtr FreeSid(IntPtr pSid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);
}
