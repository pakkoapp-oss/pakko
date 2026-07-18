using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// Hand-rolled fake — no mocking library is used anywhere in this repo. Values are keyed by
// (keyPath, valueName) so a test can simulate an absent key/value simply by not adding an entry.
file sealed class FakeRegistryReader : IRegistryReader
{
    private readonly Dictionary<(string KeyPath, string ValueName), int> _dwords = new();
    private readonly Dictionary<(string KeyPath, string ValueName), string[]> _multiStrings = new();

    public FakeRegistryReader WithDword(string keyPath, string valueName, int value)
    {
        _dwords[(keyPath, valueName)] = value;
        return this;
    }

    public FakeRegistryReader WithMultiString(string keyPath, string valueName, params string[] values)
    {
        _multiStrings[(keyPath, valueName)] = values;
        return this;
    }

    public int? GetDword(string keyPath, string valueName) =>
        _dwords.TryGetValue((keyPath, valueName), out int value) ? value : null;

    public string[]? GetMultiString(string keyPath, string valueName) =>
        _multiStrings.TryGetValue((keyPath, valueName), out string[]? value) ? value : null;
}

public sealed class GroupPolicyServiceTests
{
    private const string PolicyKeyPath = @"Software\Policies\Pakko";

    [Fact]
    public void Load_NoValuesPresent_ReturnsShippedDefaults()
    {
        GroupPolicyOptions options = GroupPolicyService.Load(new FakeRegistryReader());

        options.MotwMode.Should().Be(MotwMode.AllFiles);
        options.AllowedFormats.Should().BeNull();
        options.BlockedFormats.Should().BeNull();
        options.DisableTarExtraction.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, MotwMode.Disabled)]
    [InlineData(1, MotwMode.AllFiles)]
    [InlineData(2, MotwMode.UnsafeExtensionsOnly)]
    [InlineData(99, MotwMode.AllFiles)] // malformed/unknown value falls back to today's default
    public void Load_EnforceMotwPresent_MapsToExpectedMode(int dwordValue, MotwMode expected)
    {
        var reader = new FakeRegistryReader().WithDword(PolicyKeyPath, "EnforceMOTW", dwordValue);

        GroupPolicyService.Load(reader).MotwMode.Should().Be(expected);
    }

    [Fact]
    public void Load_EnforceMotwAbsent_DiffersFromExplicitZero()
    {
        // DWORD=0 (Disabled) must not be conflated with an absent key (AllFiles default) —
        // absent and 0 are different registry states with different effective behavior.
        GroupPolicyOptions absent = GroupPolicyService.Load(new FakeRegistryReader());
        GroupPolicyOptions explicitZero = GroupPolicyService.Load(
            new FakeRegistryReader().WithDword(PolicyKeyPath, "EnforceMOTW", 0));

        absent.MotwMode.Should().Be(MotwMode.AllFiles);
        explicitZero.MotwMode.Should().Be(MotwMode.Disabled);
    }

    [Fact]
    public void Load_AllowedAndBlockedFormatsPresent_ReadBothLists()
    {
        var reader = new FakeRegistryReader()
            .WithMultiString(PolicyKeyPath, "AllowedFormats", "zip", "tar")
            .WithMultiString(PolicyKeyPath, "BlockedFormats", "rar");

        GroupPolicyOptions options = GroupPolicyService.Load(reader);

        options.AllowedFormats.Should().BeEquivalentTo(new[] { "zip", "tar" });
        options.BlockedFormats.Should().BeEquivalentTo(new[] { "rar" });
    }

    [Fact]
    public void Load_EmptyMultiStringValue_TreatedAsAbsent()
    {
        var reader = new FakeRegistryReader()
            .WithMultiString(PolicyKeyPath, "AllowedFormats");

        GroupPolicyService.Load(reader).AllowedFormats.Should().BeNull();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Load_DisableTarExtractionPresent_MapsToExpectedBool(int dwordValue, bool expected)
    {
        var reader = new FakeRegistryReader().WithDword(PolicyKeyPath, "DisableTarExtraction", dwordValue);

        GroupPolicyService.Load(reader).DisableTarExtraction.Should().Be(expected);
    }

    [Fact]
    public void Load_DisableTarExtractionAbsent_DefaultsToFalse()
    {
        GroupPolicyService.Load(new FakeRegistryReader()).DisableTarExtraction.Should().BeFalse();
    }
}
