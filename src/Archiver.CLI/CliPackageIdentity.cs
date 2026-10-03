using System.Runtime.InteropServices;

namespace Archiver.CLI;

/// <summary>
/// T-F317: the package full name when this pakko.exe runs from the MSIX (the Store's <c>pakko</c>
/// execution alias), or null for the standalone zip/winget copy.
/// </summary>
internal static partial class CliPackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int MaxPackageFullNameLength = 128;

    public static string? CurrentPackageFullName()
    {
        uint length = MaxPackageFullNameLength + 1;
        char[] buffer = new char[length];
        int error = GetCurrentPackageFullName(ref length, ref buffer[0]);
        if (error == ErrorInsufficientBuffer && length > 0)
        {
            buffer = new char[length];
            error = GetCurrentPackageFullName(ref length, ref buffer[0]);
        }

        // APPMODEL_ERROR_NO_PACKAGE (15700) and every other failure mean "not packaged" here.
        if (error != 0 || length == 0 || length > buffer.Length)
            return null;
        return new string(buffer, 0, (int)length - 1);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, ref char packageFullName);
}
