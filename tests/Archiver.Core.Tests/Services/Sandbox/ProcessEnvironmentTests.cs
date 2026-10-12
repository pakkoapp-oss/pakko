using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

/// <summary>
/// T-F342: a child can be given changed environment variables (TMP/TEMP for libarchive's 7z
/// writer). It gets the parent's whole environment with those values replaced - a block holding
/// only the changed ones would leave tar.exe without SystemRoot and PATH.
/// </summary>
public sealed class ProcessEnvironmentTests
{
    private const string Cmd = @"C:\Windows\System32\cmd.exe";

    private static string[] Entries(char[] block) =>
        new string(block).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static async Task<string> SetOutputAsync(ProcessLaunchOptions options)
    {
        (int exitCode, string stdOut, _) = await SandboxedProcessLauncher.RunAsync(Cmd, ["/c", "set"], options, CancellationToken.None);
        exitCode.Should().Be(0);
        return stdOut;
    }

    // --- Happy path ---

    [Fact]
    public void Build_ReplacesAnOverriddenVariableAndKeepsTheRest()
    {
        KeyValuePair<string, string>[] current = [new("Path", @"C:\Windows"), new("TMP", @"C:\Temp"), new("SystemRoot", @"C:\Windows")];

        char[] block = ProcessEnvironmentBlock.Build(current, new Dictionary<string, string> { ["TMP"] = @"D:\out\t" });

        Entries(block).Should().Equal(@"Path=C:\Windows", @"SystemRoot=C:\Windows", @"TMP=D:\out\t");
        new string(block).Should().EndWith("\0\0");
    }

    [Fact]
    public async Task RunAsync_WithOverrides_ChildSeesThemAndTheParentsOtherVariables()
    {
        string value = "pakko-" + Guid.NewGuid().ToString("N");

        string output = await SetOutputAsync(new ProcessLaunchOptions(
            EnvironmentOverrides: new Dictionary<string, string> { ["TMP"] = value, ["TEMP"] = value }));

        output.Should().Contain("TMP=" + value).And.Contain("TEMP=" + value);
        output.Should().ContainEquivalentOf("SystemRoot=" + Environment.GetEnvironmentVariable("SystemRoot"));
        output.Should().ContainEquivalentOf("PATH=");
    }

    [Fact]
    public async Task RunAsync_WithoutOverrides_ChildInheritsTheParentsEnvironment()
    {
        string output = await SetOutputAsync(new ProcessLaunchOptions());

        output.Should().ContainEquivalentOf("TMP=" + Environment.GetEnvironmentVariable("TMP"));
    }

    // --- Security & Boundary ---

    [Fact]
    public void Build_OverrideNameInAnotherCase_ReplacesAndDoesNotAddASecondEntry()
    {
        KeyValuePair<string, string>[] current = [new("Tmp", @"C:\Temp")];

        char[] block = ProcessEnvironmentBlock.Build(current, new Dictionary<string, string> { ["TMP"] = "x" });

        Entries(block).Should().ContainSingle().Which.Should().EndWith("=x");
    }

    [Fact]
    public void Build_SortsNamesWithoutRegardToCase()
    {
        KeyValuePair<string, string>[] current = [new("b", "2"), new("A", "1"), new("C", "3")];

        Entries(ProcessEnvironmentBlock.Build(current, new Dictionary<string, string>())).Should().Equal("A=1", "b=2", "C=3");
    }

    [Fact]
    public async Task RunAsync_NonAsciiValue_ArrivesUnchanged()
    {
        // U+0456 and U+00E9: the block is UTF-16, so no code page is involved.
        string value = "t\u0456\u00E9-" + Guid.NewGuid().ToString("N");
        string file = Path.Combine(Path.GetTempPath(), "pakko-env-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            // cmd /u writes "set"'s redirected output as UTF-16.
            (int exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(Cmd, ["/u", "/c", "set PAKKO_T>" + file],
                new ProcessLaunchOptions(EnvironmentOverrides: new Dictionary<string, string> { ["PAKKO_T"] = value }), CancellationToken.None);

            exitCode.Should().Be(0);
            File.ReadAllText(file, System.Text.Encoding.Unicode).Should().Contain("PAKKO_T=" + value);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // --- Misuse ---

    [Theory]
    [InlineData("A=B", "x")]
    [InlineData("", "x")]
    [InlineData("A\0B", "x")]
    [InlineData("A", "x\0y")]
    public void Build_NameOrValueThatWouldBreakTheBlock_Throws(string name, string value)
    {
        Action act = () => ProcessEnvironmentBlock.Build([], new Dictionary<string, string> { [name] = value });

        act.Should().Throw<ArgumentException>();
    }

    // --- Error path ---

    [Fact]
    public void Build_NothingAtAll_IsStillATerminatedBlock() =>
        ProcessEnvironmentBlock.Build([], new Dictionary<string, string>()).Should().Equal('\0', '\0');
}
