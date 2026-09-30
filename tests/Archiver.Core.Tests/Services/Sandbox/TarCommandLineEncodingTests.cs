using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F266: tar.exe reads its command line through the ANSI code page with best-fit mapping, so a
// fullwidth quote (U+FF02) became '"' and split one quoted argument into several — option
// injection. Only a string that converts to that code page exactly, with no best-fit and no
// default character, and converts back to itself, may reach tar.exe. Explicit code pages keep
// these expectations independent of the machine's own ANSI code page.
public sealed class TarCommandLineEncodingTests
{
    [Theory]
    [InlineData("plain.txt", 1252)]
    [InlineData("caf\u00E9.txt", 1252)]
    [InlineData("\u0414\u043E\u043A.txt", 1251)]
    [InlineData("tick \u2713.txt", 65001)]
    [InlineData("x\uFF02y", 65001)]
    [InlineData("", 1251)]
    public void IsRepresentable_ExactInCodePage_True(string value, int codePage)
    {
        TarCommandLineEncoding.IsRepresentable(value, (uint)codePage).Should().BeTrue();
    }

    [Theory]
    [InlineData("x\uFF02 --version \uFF02", 1252)]
    [InlineData("x\uFF02 --version \uFF02", 1251)]
    [InlineData("tick \u2713.txt", 1251)]
    [InlineData("caf\u00E9.txt", 1251)]
    [InlineData("\u0414\u043E\u043A.txt", 1252)]
    [InlineData("\uFF0E\uFF0E\uFF3Cx", 1252)]
    public void IsRepresentable_BestFitOrMissingInCodePage_False(string value, int codePage)
    {
        TarCommandLineEncoding.IsRepresentable(value, (uint)codePage).Should().BeFalse();
    }

    // Built in the body: the test runner cannot serialize an unpaired surrogate as test data.
    [Theory]
    [InlineData(65001)]
    [InlineData(1252)]
    public void IsRepresentable_UnpairedSurrogate_False(int codePage)
    {
        string value = "a" + (char)0xD800 + "b";
        TarCommandLineEncoding.IsRepresentable(value, (uint)codePage).Should().BeFalse();
    }

    // T-F283: the names tar.exe reads from its stdin list go through the same code page.
    [Fact]
    public void EncodeLines_EachLineEndsWithNewlineInCodePage()
    {
        byte[] bytes = TarCommandLineEncoding.EncodeLines(["-C", "\u0414\u043E\u043A"], 1251);

        bytes.Should().Equal((byte)'-', (byte)'C', (byte)'\n', 0xC4, 0xEE, 0xEA, (byte)'\n');
    }

    [Fact]
    public void EncodeLines_BestFitName_Throws()
    {
        Action act = () => TarCommandLineEncoding.EncodeLines(["ok", "x\uFF02y"], 1252);

        act.Should().Throw<TarArgumentEncodingException>();
    }

    [Fact]
    public async Task Launcher_StdInData_ReachesChild()
    {
        (int exitCode, string stdOut, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\findstr.exe", ["x"],
            new ProcessLaunchOptions(StdInData: "xa\nyb\nxc\n"u8.ToArray()), CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Should().Equal("xa", "xc");
    }

    // A child that exits without reading its stdin must not leave the call waiting on the write.
    [Fact]
    public async Task Launcher_StdInDataChildNeverReads_Completes()
    {
        Task<(int, string, string)> run = SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\hostname.exe", [],
            new ProcessLaunchOptions(StdInData: new byte[4 * 1024 * 1024]), CancellationToken.None);

        (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(run);
        (await run).Item1.Should().Be(0);
    }

    [Fact]
    public async Task Launcher_StdInDataCancelled_ThrowsPromptly()
    {
        using var cts = new CancellationTokenSource();
        // Not CancellationTokenSource(TimeSpan): its timer fires on the ThreadPool, which a full
        // parallel CI run can starve past PING's whole minute (same as T-F279's test, 3798127).
        var canceller = new Thread(() =>
        {
            using var never = new ManualResetEventSlim();
            never.Wait(TimeSpan.FromMilliseconds(500));
            cts.Cancel();
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        canceller.Start();
        Task<(int, string, string)> run = SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\PING.EXE", ["-n", "60", "127.0.0.1"],
            new ProcessLaunchOptions(StdInData: new byte[4 * 1024 * 1024]), cts.Token);

        await run.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        canceller.Join();
    }

    [Fact]
    public async Task Launcher_UnrepresentableArgument_RefusesBeforeCreatingProcess()
    {
        // Built so it is unrepresentable in every ANSI code page, UTF-8 included (a lone surrogate).
        string argument = "x\uFF02 --version \uFF02" + (char)0xD800;
        bool started = false;

        Func<Task> act = () => SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\tar.exe", ["-tf", argument],
            new ProcessLaunchOptions(OnProcessStarted: _ => started = true), CancellationToken.None);

        await act.Should().ThrowAsync<TarArgumentEncodingException>();
        started.Should().BeFalse();
    }
}
