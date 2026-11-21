using System.Runtime.InteropServices;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470, T-F562: a test that needs the real archive helper chosen by the real gate, which Pakko does
/// only in a process built for the machine it runs on, x64 or ARM64. Skipped elsewhere: an x64 process
/// emulated on ARM64 reads with tar.exe, and an x64 helper run there keeps its image locked for a
/// moment after it exits (T-F546).
/// </summary>
public sealed class NativeFactAttribute : FactAttribute
{
    public NativeFactAttribute()
    {
        Architecture process = RuntimeInformation.ProcessArchitecture;
        if (process is not (Architecture.X64 or Architecture.Arm64) || process != RuntimeInformation.OSArchitecture)
            Skip = $"not a native x64 or ARM64 process ({process} on {RuntimeInformation.OSArchitecture}); Pakko reads with tar.exe here";
    }
}
