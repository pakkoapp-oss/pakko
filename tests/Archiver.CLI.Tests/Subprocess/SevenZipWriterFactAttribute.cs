using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// A test that needs a tar.exe with the 7z writer: the probe Pakko itself runs
/// (<see cref="ITarService.CanCreateSevenZipAsync"/>), as in Archiver.Core.IntegrationTests'
/// attribute of the same name. Skipped where this system's tar.exe has none.
/// </summary>
public sealed class SevenZipWriterFactAttribute : FactAttribute
{
    public SevenZipWriterFactAttribute()
    {
        if (!new TarSandboxedService(new GroupPolicyOptions()).CanCreateSevenZipAsync(CancellationToken.None).GetAwaiter().GetResult())
            Skip = "this system's tar.exe does not write 7z";
    }
}

/// <summary>A theory that needs a tar.exe with the 7z writer; see <see cref="SevenZipWriterFactAttribute"/>.</summary>
public sealed class SevenZipWriterTheoryAttribute : TheoryAttribute
{
    public SevenZipWriterTheoryAttribute()
    {
        if (!new TarSandboxedService(new GroupPolicyOptions()).CanCreateSevenZipAsync(CancellationToken.None).GetAwaiter().GetResult())
            Skip = "this system's tar.exe does not write 7z";
    }
}
