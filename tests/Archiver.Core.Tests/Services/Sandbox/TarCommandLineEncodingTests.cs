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
