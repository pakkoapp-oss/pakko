using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Archiver.Core.IntegrationTests;

internal static partial class HardLink
{
    /// <summary>Gives <paramref name="existingFile"/> a second name on the same volume.</summary>
    public static void Create(string linkPath, string existingFile)
    {
        if (!CreateHardLinkW(linkPath, existingFile, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
}
