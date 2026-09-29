using System.Runtime.InteropServices;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// <see cref="IntegrationAttribute"/>, also skipped on an ARM64 OS: its tar.exe exits with
/// 0xC0000005 on the non-ASCII name this test's fixture passes it (T-F290). The OS
/// architecture, not the process's, since the crash is in the OS's own tar.exe.
/// </summary>
public sealed class IntegrationNotOnArm64Attribute : FactAttribute
{
    public IntegrationNotOnArm64Attribute()
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
        else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            Skip = "ARM64 tar.exe crashes on a non-ASCII name argument (T-F290)";
    }
}
