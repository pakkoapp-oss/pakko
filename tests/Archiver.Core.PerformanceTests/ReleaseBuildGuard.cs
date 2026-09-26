using System.Diagnostics;
using System.Reflection;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F272: the one-large-file ratios are calibrated against an optimized Archiver.Core. A Debug
/// build of Core runs its managed stream-copy loop several times slower while 7za.exe (native)
/// is unaffected, so a Debug run fails the ratio with a misleading "real regression" message.
/// </summary>
public static class ReleaseBuildGuard
{
    public static void RequireOptimizedCore()
    {
        var debuggable = typeof(ZipArchiveService).Assembly.GetCustomAttribute<DebuggableAttribute>();
        bool optimized = debuggable is null || !debuggable.IsJITOptimizerDisabled;
        optimized.Should().BeTrue(
            because: "the VeryLarge ratio tests are calibrated in Release — run " +
                     "`dotnet test -c Release --filter \"Category=VeryLarge\"`, not a Debug build");
    }
}
