using System.Runtime.InteropServices;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: a test that needs the real archive helper chosen by the real gate, which Pakko does only
/// in a native x64 process. Skipped elsewhere: on the ARM64 canary the gate reads with tar.exe, and
/// the x64 helper run there under emulation keeps its image locked for a moment after it exits.
/// </summary>
public sealed class NativeX64FactAttribute : FactAttribute
{
    public NativeX64FactAttribute()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || RuntimeInformation.OSArchitecture != Architecture.X64)
            Skip = $"not a native x64 process ({RuntimeInformation.ProcessArchitecture} on {RuntimeInformation.OSArchitecture}); Pakko reads with tar.exe here";
    }
}
