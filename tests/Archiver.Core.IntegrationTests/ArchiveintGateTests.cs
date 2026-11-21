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

    [NativeFact]
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

    // T-F562: Windows on ARM64 ships its own archiveint.dll; a native ARM64 Pakko reads through the
    // ARM64 helper as x64 does.
    [Fact]
    public async Task NativeArm64_WithAnArm64Helper_ChoosesTheHelper()
    {
        ArchiveintGate gate = Gate(machine: () => Arm64, helperMachine: _ => Arm64, probe: (_, _) => Task.FromResult(Probe("libarchive 3.8.9 zlib/1.3.1")));

        (await gate.DecideAsync()).UsesHelper.Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x014C)]
    [InlineData((ushort)0x01C4)]
    public async Task NeitherNativeX64NorNativeArm64_ReadsWithTarExe(ushort machine)
    {
        ArchiveintGate gate = Gate(machine: () => machine, helperMachine: _ => machine);

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("not a native x64 or ARM64 process");
    }

    // T-F562: an x64 helper beside an ARM64 Pakko (or the reverse) would run emulated in the sandbox,
    // where Windows keeps its image open after it exits (T-F546).
    [Theory]
    [InlineData(Amd64, Arm64)]
    [InlineData(Arm64, Amd64)]
    public async Task AHelperBuiltForAnotherMachine_ReadsWithTarExe(ushort process, ushort helper)
    {
        int probes = 0;
        ArchiveintGate gate = Gate(machine: () => process, helperMachine: _ => helper,
            probe: (_, _) => { probes++; return Task.FromResult(Probe("libarchive 3.8.9")); });

        ArchiveintDecision decision = await gate.DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("the archive helper is not built for this machine");
        probes.Should().Be(0);
    }

    [Fact]
    public async Task AHelperWhoseMachineCannotBeRead_ReadsWithTarExe()
    {
        ArchiveintDecision decision = await Gate(helperMachine: _ => null).DecideAsync();

        decision.UsesHelper.Should().BeFalse();
        decision.Cause.Should().Contain("the archive helper is not built for this machine");
    }

    // T-F562: IsWow64Process2 reports no process machine for x64 emulated on ARM64 (it is not WOW64),
    // so the process's own architecture decides whether it runs natively.
    [Theory]
    [InlineData(System.Runtime.InteropServices.Architecture.X64, (ushort)0, Amd64, Amd64)]
    [InlineData(System.Runtime.InteropServices.Architecture.Arm64, (ushort)0, Arm64, Arm64)]
    [InlineData(System.Runtime.InteropServices.Architecture.X64, (ushort)0, Arm64, null)]
    [InlineData(System.Runtime.InteropServices.Architecture.Arm64, (ushort)0, Amd64, null)]
    [InlineData(System.Runtime.InteropServices.Architecture.X86, (ushort)0x014C, Amd64, null)]
    [InlineData(System.Runtime.InteropServices.Architecture.X86, (ushort)0x014C, Arm64, null)]
    public void NativeMachine_IsTheHostOnlyForAProcessBuiltForIt(System.Runtime.InteropServices.Architecture process, ushort wow64Machine, ushort host, ushort? expected) =>
        ArchiveintGate.NativeMachineOf(process, wow64Machine, host).Should().Be(expected);

    [Fact]
    public void PeMachine_OfTheBuiltHelper_IsItsPlatform()
    {
        ArchiveintGate.PeMachineOf(ArchiveHelperBuild.ExePathFor("x64")).Should().Be(Amd64);
        ArchiveintGate.PeMachineOf(ArchiveHelperBuild.ExePathFor("ARM64")).Should().Be(Arm64);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 0, false)]
    [InlineData(64, 0x40, true)]
    [InlineData(200, 0x7FFFFFF0, true)]
    [InlineData(200, -8, true)]
    [InlineData(200, 0x10, true)]
    [InlineData(200, 0x40, false)]
    public void PeMachine_OfAFileThatIsNoImage_IsNull(int length, int peOffset, bool signature) =>
        WithImage(length, peOffset, signature, path => ArchiveintGate.PeMachineOf(path).Should().BeNull());

    [Fact]
    public void PeMachine_OfTheSmallestImageShape_IsItsMachineField() =>
        WithImage(200, 0x40, signature: true, path => ArchiveintGate.PeMachineOf(path).Should().Be(Amd64));

    // "MZ", the PE header's offset at 0x3C, and there "PE\0\0" (or not) and the x64 machine field.
    private static void WithImage(int length, int peOffset, bool signature, Action<string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "pakko-pe-" + Guid.NewGuid().ToString("N") + ".exe");
        byte[] bytes = new byte[length];
        if (length >= 2)
        {
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
        }
        if (length >= 0x40)
            BitConverter.GetBytes(peOffset).CopyTo(bytes, 0x3C);
        if (peOffset >= 0 && peOffset <= length - 6)
        {
            if (signature)
            {
                bytes[peOffset] = (byte)'P';
                bytes[peOffset + 1] = (byte)'E';
            }
            BitConverter.GetBytes(Amd64).CopyTo(bytes, peOffset + 4);
        }
        File.WriteAllBytes(path, bytes);
        try
        {
            check(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PeMachine_OfAMissingFile_IsNull() =>
        ArchiveintGate.PeMachineOf(Path.Combine(Path.GetTempPath(), "pakko-pe-" + Guid.NewGuid().ToString("N") + ".exe")).Should().BeNull();

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

    // T-F490: the checks before the probe's first await (the signature check of archiveint.dll
    // among them) ran on the caller's thread, the App's UI thread before its first window.
    [Fact]
    public async Task DecideAsync_ReturnsBeforeTheSignatureCheckEnds()
    {
        using var release = new ManualResetEventSlim();
        bool checkEnded = false;
        ArchiveintGate gate = Gate(
            verify: _ => { release.Wait(TimeSpan.FromSeconds(10)); Volatile.Write(ref checkEnded, true); return true; },
            probe: (_, _) => Task.FromResult(Probe("libarchive 3.8.8")));

        Task<ArchiveintDecision> decision = gate.DecideAsync();
        bool endedBeforeReturn = Volatile.Read(ref checkEnded);
        release.Set();

        endedBeforeReturn.Should().BeFalse();
        (await decision).UsesHelper.Should().BeTrue();
    }

    // --- Harness ---

    // Every condition but the one a test changes: a helper file that exists, native x64, a helper
    // built for it, a signed DLL, and a probe that must be given.
    private static ArchiveintGate Gate(
        GroupPolicyOptions? policy = null, string? helperPath = null, Func<ushort?>? machine = null, Func<string, bool>? verify = null,
        Func<string, CancellationToken, Task<ArchiveHelperOutput>>? probe = null, TimeSpan? timeout = null, Func<string, ushort?>? helperMachine = null) =>
        new(policy ?? new GroupPolicyOptions(), helperPath ?? typeof(ArchiveintGateTests).Assembly.Location, machine ?? (() => Amd64), verify ?? (_ => true),
            probe ?? ((_, _) => throw new InvalidOperationException("this test gives no probe")), timeout, helperMachine ?? (_ => Amd64));

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
