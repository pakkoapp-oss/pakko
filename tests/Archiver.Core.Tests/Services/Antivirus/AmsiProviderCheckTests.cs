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

    // Shell.Application: registered in HKLM on every Windows, %SystemRoot%\system32\shell32.dll as
    // REG_EXPAND_SZ, so this also pins that the path is expanded before the file is looked for.
    [Fact]
    public void IsLoadable_RegisteredSystemClass_IsTrue() =>
        AmsiProviderCheck.IsLoadable(new Guid("13709620-C279-11CE-A49E-444553540000")).Should().BeTrue();

    [Fact]
    public void IsLoadable_UnregisteredClsid_IsFalse() =>
        AmsiProviderCheck.IsLoadable(Guid.NewGuid()).Should().BeFalse();

    private const string Defender = "{2781761E-28E0-4109-99FE-B9D127C57AFE}";
    private const string TrendMicro = "{6138A34F-DCA4-48D0-95CC-51E1D8F30B58}";

    // T-F448: a provider measured to answer S_OK + AMSI_RESULT_CLEAN for EICAR, alone, gives no
    // verdict to trust; beside any other provider amsi.dll could load, that one answers too. The
    // expected state goes by name: an internal enum cannot be a public theory's parameter type (CS0051).
    [Theory]
    [InlineData(new string[0], new string[0], nameof(AmsiProviderState.None))]
    [InlineData(new[] { TrendMicro }, new[] { TrendMicro }, nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { "{6138a34f-dca4-48d0-95cc-51e1d8f30b58}" }, new string[0], nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { "6138A34F-DCA4-48D0-95CC-51E1D8F30B58" }, new string[0], nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { Defender }, new[] { Defender }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { TrendMicro, Defender }, new[] { TrendMicro, Defender }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { "not-a-guid" }, new string[0], nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { "" }, new string[0], nameof(AmsiProviderState.Usable))]
    // No listed provider: nothing is checked for loading, as before T-F448.
    [InlineData(new[] { Defender }, new string[0], nameof(AmsiProviderState.Usable))]
    // Beside a listed provider, only another one amsi.dll could load counts.
    [InlineData(new[] { TrendMicro, Defender }, new[] { TrendMicro }, nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { TrendMicro, Defender }, new[] { Defender }, nameof(AmsiProviderState.Usable))]
    [InlineData(new[] { "not-a-guid", TrendMicro }, new string[0], nameof(AmsiProviderState.OnlyNonReporting))]
    [InlineData(new[] { "", TrendMicro }, new string[0], nameof(AmsiProviderState.OnlyNonReporting))]
    public void Classify_MapsRegisteredProviders(string[] subKeyNames, string[] loadable, string expected)
    {
        HashSet<Guid> loadableIds = [.. loadable.Select(Guid.Parse)];
        var asked = new List<Guid>();

        string state = AmsiProviderCheck.Classify(subKeyNames, clsid => { asked.Add(clsid); return loadableIds.Contains(clsid); }).ToString();

        state.Should().Be(expected);
        if (!subKeyNames.Any(n => Guid.TryParse(n, out Guid g) && g == Guid.Parse(TrendMicro)))
            asked.Should().BeEmpty("without a listed provider no DLL is looked up");
    }
}
