using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F370: the Claude Code PreToolUse hook blocks bare python and dotnet /p: in the Bash tool. It
// runs the real script through pwsh with the payload Claude Code sends; exit 2 blocks the command.
public sealed class AgentBashHookTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [InlineData("python foo.py")]
    [InlineData("python3 -c \"print(1)\"")]
    [InlineData("cd /c/repo && python script.py")]
    [InlineData("echo hi; python3 x.py")]
    [InlineData("echo \"a \\\" b\" && python x.py")]
    [InlineData("ls | python -")]
    [InlineData("v=$(python3 -V)")]
    [InlineData("FOO=1 python x.py")]
    [InlineData("first line\npython x.py")]
    [InlineData("dotnet build src/A.csproj /p:Platform=x64")]
    [InlineData("cd repo && dotnet publish x \"/p:Foo=1\"")]
    [InlineData("dotnet publish src/A.csproj \\\n    /p:Configuration=Release")]
    public void ForbiddenCommand_IsBlocked(string command)
    {
        (int exitCode, string stderr) = RunHook(Payload(command));

        exitCode.Should().Be(2);
        stderr.Should().Contain("T-F370");
    }

    [Theory]
    [InlineData("py -3 script.py")]
    [InlineData("ls -la")]
    [InlineData("dotnet test --filter \"Category!=Slow&Category!=VeryLarge\"")]
    [InlineData("grep -n \"python\" CLAUDE.md")]
    [InlineData("git commit -m \"use python; dotnet build /p:x\"")]
    [InlineData("echo dotnet /p:x")]
    [InlineData("git commit -F - <<'EOF'\npython x.py\ndotnet build /p:A=1\nEOF")]
    [InlineData("cat note.txt | grep python3")]
    public void AllowedCommand_Passes(string command)
    {
        RunHook(Payload(command)).ExitCode.Should().Be(0);
    }

    [Fact]
    public void PayloadWithoutACommand_Passes()
    {
        RunHook("{\"tool_name\":\"Bash\"}").ExitCode.Should().Be(0);
    }

    [Fact]
    public void PayloadThatIsNotJson_FailsWithoutBlocking()
    {
        (int exitCode, string stderr) = RunHook("not json");

        exitCode.Should().Be(1, "exit 2 would block; a broken payload must not");
        stderr.Should().Contain("not JSON");
    }

    [Fact]
    public void ProjectSettings_RunTheHookBeforeEveryBashCall()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, ".claude", "settings.json")));
        JsonElement entry = settings.RootElement.GetProperty("hooks").GetProperty("PreToolUse")
            .EnumerateArray().Single(e => e.GetProperty("matcher").GetString() == "Bash");

        entry.GetProperty("hooks").EnumerateArray().Select(h => h.GetProperty("command").GetString())
            .Should().ContainSingle(c => c!.Contains("scripts/hooks/Test-AgentBashCommand.ps1", StringComparison.Ordinal));
    }

    private static string Payload(string command) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });

    private static (int ExitCode, string Stderr) RunHook(string payload)
    {
        // Claude Code finds pwsh on PATH too; it lives in Program Files on CI and may be the Store build locally.
        string pwsh = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, "pwsh.exe"))
            .First(File.Exists);
        var start = new ProcessStartInfo(pwsh)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string arg in (string[])["-NoProfile", "-NonInteractive", "-File", Path.Combine(RepoRoot, "scripts", "hooks", "Test-AgentBashCommand.ps1")])
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start)!;
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stderr.Result);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
