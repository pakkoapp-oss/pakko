using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: the gate decides once per process whether the archive helper or tar.exe reads; every
/// condition it checks, the probe's verdict, and that the decision stands.
/// </summary>
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ArchiveintGateTests
{
    private const ushort Amd64 = 0x8664;
    private const ushort Arm64 = 0xAA64;

    // --- Happy path: the real probe, the real DLL ---

    [Fact]
    public async Task EveryConditionMet_ChoosesTheHelper()
    {
        var gate = new ArchiveintGate(new GroupPolicyOptions(), ArchiveHelperBuild.ExePath);

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeTrue(decision.Describe());
        decision.DllVersion.Should().StartWith("libarchive 3.");
        decision.Describe().Should().StartWith("tar-family reader: archive helper; archiveint.dll: libarchive 3.");
    }

    [Fact]
    public async Task AForgedGoodProbe_ChoosesTheHelper_WithItsCapabilities()
    {
        ArchiveintGate gate = Gate(probe: (_, _) => Task.FromResult(Probe("libarchive 3.8.8 zlib/1.3.1")));

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeTrue(decision.Describe());
        (await gate.HelperAsync(CancellationToken.None))!.Capabilities.Should().ContainKey("tar").WhoseValue.Should().Be(0);
    }

    // --- Each condition falls back to tar.exe ---

    [Fact]
    public async Task GroupPolicy_DisablingTar_NeverProbes()
    {
        int probes = 0;
        ArchiveintGate gate = Gate(new GroupPolicyOptions { DisableTarExtraction = true }, probe: (_, _) => { probes++; return Task.FromResult(Probe("libarchive 3.8.8")); });

        (await gate.DecideAsync()).UsesHelper.Should().BeFalse();
        probes.Should().Be(0);
    }

    [Fact]
    public async Task NoHelperFile_ReadsWithTarExe()
    {
        ArchiveintGate gate = Gate(helperPath: Path.Combine(Path.GetTempPath(), "no-such-helper-" + Guid.NewGuid().ToString("N") + ".exe"));

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("no archive helper");
    }

    [Theory]
    [InlineData(Arm64)]
    [InlineData((ushort)0x014C)]
    public async Task NotANativeX64Machine_ReadsWithTarExe(ushort machine)
    {
        ArchiveintGate gate = Gate(machine: () => machine);

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("not a native x64 process");
    }

    [Fact]
    public async Task AMachineNotKnown_ReadsWithTarExe() =>
        (await Gate(machine: () => null).DecideAsync()).UsesHelper.Should().BeFalse();

    [Fact]
    public async Task AnUnsignedArchiveint_ReadsWithTarExe()
    {
        ArchiveintGate gate = Gate(verify: _ => false);

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("not signed");
    }

    [Theory]
    [InlineData("libarchive 3.4.3 zlib/1.2.11")]
    [InlineData("libarchive 3.9.0 zlib/1.3.1")]
    [InlineData("libarchive 4.0.0")]
    [InlineData("libarchive 3.10.1")]
    [InlineData("bsdtar 3.8.8")]
    [InlineData("libarchive 3.8")]
    public async Task AVersionNotMeasured_ReadsWithTarExe(string version)
    {
        ArchiveintDecision decision = await Gate(probe: (_, _) => Task.FromResult(Probe(version))).DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.DllVersion.Should().Be(version);
    }

    [Theory]
    [InlineData("libarchive 3.5.2 zlib/1.2.11")]
    [InlineData("libarchive 3.7.4")]
    [InlineData("libarchive 3.8.9 zlib/1.3.1 liblzma/5.4.4")]
    public async Task AMeasuredVersion_ChoosesTheHelper(string version) =>
        (await Gate(probe: (_, _) => Task.FromResult(Probe(version))).DecideAsync()).UsesHelper.Should().BeTrue();

    [Fact]
    public async Task AMissingExport_ReadsWithTarExe()
    {
        ArchiveintDecision decision = await Gate(probe: (_, _) => Task.FromResult(Probe("libarchive 3.8.8", missing: "archive_read_support_format_rar5"))).DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("archive_read_support_format_rar5");
    }

    [Fact]
    public async Task AProbeThatDidNotFinish_ReadsWithTarExe()
    {
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Probe);
        output.Feed(Encoding.ASCII.GetBytes("V " + Hex("libarchive 3.8.8")));

        (await Gate(probe: (_, _) => Task.FromResult(output)).DecideAsync()).UsesHelper.Should().BeFalse();
    }

    [Fact]
    public async Task AForgedProbeLine_ReadsWithTarExe()
    {
        ArchiveintGate gate = Gate(probe: (_, _) =>
        {
            var output = new ArchiveHelperOutput(ArchiveHelperMode.Probe);
            output.Feed("E 0 0 0 0 - - - - -"u8);
            return Task.FromResult(output);
        });

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("the probe failed");
    }

    [Fact]
    public async Task AProbeThatHangs_TimesOut_ToTarExe()
    {
        ArchiveintGate gate = Gate(probe: (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => Probe("libarchive 3.8.8"), TaskScheduler.Default), timeout: TimeSpan.FromMilliseconds(200));

        ArchiveintDecision decision = await gate.DecideAsync().WaitAsync(TimeSpan.FromSeconds(30));

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("timed out");
    }

    [Fact]
    public async Task AProbeThatIgnoresItsToken_StillTimesOut()
    {
        var never = new TaskCompletionSource<ArchiveHelperOutput>();
        ArchiveintGate gate = Gate(probe: (_, _) => never.Task, timeout: TimeSpan.FromMilliseconds(200));

        (await gate.DecideAsync().WaitAsync(TimeSpan.FromSeconds(30))).Cause.Should().Contain("timed out");
    }

    // --- The decision stands for the process ---

    [Fact]
    public async Task TheDecision_IsMadeOnce()
    {
        int probes = 0;
        ArchiveintGate gate = Gate(probe: (_, _) => { Interlocked.Increment(ref probes); return Task.FromResult(Probe("libarchive 3.8.8")); });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => gate.DecideAsync()));
        await gate.HelperAsync(CancellationToken.None);

        probes.Should().Be(1);
    }

    [Fact]
    public async Task AFailedProbe_IsNotRetried()
    {
        int probes = 0;
        ArchiveintGate gate = Gate(probe: (_, _) => { probes++; throw new IOException("the sandbox would not start"); });

        (await gate.DecideAsync()).UsesHelper.Should().BeFalse();
        (await gate.DecideAsync()).UsesHelper.Should().BeFalse();
        (await gate.HelperAsync(CancellationToken.None)).Should().BeNull();

        probes.Should().Be(1);
    }

    [Fact]
    public async Task ACancelledFirstCaller_DoesNotBecomeTheVerdict()
    {
        var release = new TaskCompletionSource();
        ArchiveintGate gate = Gate(probe: async (_, _) => { await release.Task; return Probe("libarchive 3.8.8"); });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> first = () => gate.HelperAsync(cancelled.Token);
        await first.Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();

        (await gate.HelperAsync(CancellationToken.None)).Should().NotBeNull();
    }

    // --- Harness ---

    // Every condition but the one a test changes: a helper file that exists, native x64, a signed
    // DLL, and a probe that must be given.
    private static ArchiveintGate Gate(
        GroupPolicyOptions? policy = null, string? helperPath = null, Func<ushort?>? machine = null, Func<string, bool>? verify = null,
        Func<string, CancellationToken, Task<ArchiveHelperOutput>>? probe = null, TimeSpan? timeout = null) =>
        new(policy ?? new GroupPolicyOptions(), helperPath ?? typeof(ArchiveintGateTests).Assembly.Location, machine ?? (() => Amd64), verify ?? (_ => true),
            probe ?? ((_, _) => throw new InvalidOperationException("this test gives no probe")), timeout);

    private static ArchiveHelperOutput Probe(string version, string? missing = null)
    {
        var output = new ArchiveHelperOutput(ArchiveHelperMode.Probe);
        output.Feed(Encoding.ASCII.GetBytes("V " + Hex(version)));
        if (missing is not null)
            output.Feed(Encoding.ASCII.GetBytes("X " + missing));
        foreach (string name in (string[])["tar", "cpio", "7zip", "rar", "rar5", "cab", "gzip", "bzip2", "compress", "xz", "lzma", "lzip", "zstd"])
            output.Feed(Encoding.ASCII.GetBytes("C " + name + " 0"));
        output.Feed("D"u8);
        output.Finish(0);
        return output;
    }

    private static string Hex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));
}
