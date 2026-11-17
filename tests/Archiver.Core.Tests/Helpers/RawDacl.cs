using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// Batch 5k: a file's or folder's DACL as SDDL, read and written through a handle to the name itself
/// (a link is not followed) with the low-level calls, which neither recompute inheritance nor walk a
/// tree: a test can set an entry flagged inherited that no parent gives, and see whether it survives.
/// </summary>
internal static partial class RawDacl
{
    private const uint DaclInformation = 4;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;

    public static string Sddl(string path)
    {
        using SafeFileHandle handle = Open(path, ReadControl);
        _ = GetKernelObjectSecurity(handle, DaclInformation, null, 0, out uint needed);
        byte[] descriptor = new byte[needed];
        if (!GetKernelObjectSecurity(handle, DaclInformation, descriptor, needed, out _))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), path);
        if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor, 1, DaclInformation, out IntPtr text, out _))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), path);
        try
        {
            return Marshal.PtrToStringUni(text)!;
        }
        finally
        {
            LocalFree(text);
        }
    }

    public static void Write(string path, string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out IntPtr descriptor, out uint length))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), sddl);
        try
        {
            byte[] bytes = new byte[length];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            using SafeFileHandle handle = Open(path, WriteDac);
            if (!SetKernelObjectSecurity(handle, DaclInformation, bytes))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), path);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static SafeFileHandle Open(string path, uint access)
    {
        SafeFileHandle handle = CreateFileW(path, access, 7, 0, 3, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, path);
        }
        return handle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[]? descriptor, uint length, out uint needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[] descriptor);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptorW(byte[] descriptor, uint revision, uint information, out IntPtr text, out uint length);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out IntPtr descriptor, out uint length);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
