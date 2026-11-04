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

    [Fact]
    public void GetState_NeverThrows()
    {
        Action act = () => AmsiProviderCheck.GetState();

        act.Should().NotThrow();
    }

    private const string Defender = "{2781761E-28E0-4109-99FE-B9D127C57AFE}";
    private const string TrendMicro = "{6138A34F-DCA4-48D0-95CC-51E1D8F30B58}";

    // T-F448: a provider measured to answer S_OK + AMSI_RESULT_CLEAN for EICAR, alone, gives no
    // verdict to trust; beside any other provider amsi.dll still asks that one. The expected state
    // goes by name: an internal enum cannot be a public theory's parameter type (CS0051).
    [Theory]
    [InlineData(new string[0], nameof(AmsiProviderState.None))]
    [InlineData(new[] { TrendMicro }, nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { "{6138a34f-dca4-48d0-95cc-51e1d8f30b58}" }, nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { "6138A34F-DCA4-48D0-95CC-51E1D8F30B58" }, nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { TrendMicro, TrendMicro }, nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { Defender }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { TrendMicro, Defender }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { "not-a-guid" }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { "not-a-guid", TrendMicro }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { "" }, nameof(AmsiProviderState.Usable))]
    public void Classify_MapsRegisteredProviders(string[] subKeyNames, string expected)
    {
        AmsiProviderCheck.Classify(subKeyNames).ToString().Should().Be(expected);
    }
}
