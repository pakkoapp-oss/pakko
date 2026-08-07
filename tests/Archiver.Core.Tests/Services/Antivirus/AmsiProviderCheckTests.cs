using System.Runtime.Versioning;
using Archiver.Core.Services.Antivirus;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Antivirus;

// T-F146: smoke test only — this dev machine's actual registered-provider state isn't something a
// unit test should assert a specific value for (would make the suite depend on this machine's AV
// configuration). What matters is that it never throws, matching GroupPolicyService.Load's own
// "never throws" discipline for registry reads.
// [SupportedOSPlatform("windows")] on the class itself, not a #pragma — IsAnyProviderRegistered
// calls Microsoft.Win32.Registry, which the BCL itself marks Windows-only (same reason
// Win32RegistryReader carries the attribute); this whole test project only ever runs on Windows.
[SupportedOSPlatform("windows")]
public sealed class AmsiProviderCheckTests
{
    [Fact]
    public void IsAnyProviderRegistered_NeverThrows()
    {
        Action act = () => AmsiProviderCheck.IsAnyProviderRegistered();

        act.Should().NotThrow();
    }
}
