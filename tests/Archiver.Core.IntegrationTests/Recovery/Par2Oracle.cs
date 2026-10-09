using System.Diagnostics;

namespace Archiver.Core.IntegrationTests.Recovery;

/// <summary>
/// The PAR2 tools Pakko's sets are checked against (T-F275), downloaded by
/// <c>scripts/Get-Par2Oracles.ps1</c> into <c>artifacts/par2-oracles</c> (docs/TESTING.md, "PAR2
/// Oracles"). Started by absolute path, never through PATH.
/// </summary>
internal static class Par2Oracle
{
    public const string Par2cmdline = "par2cmdline";
    public const string Turbo = "par2cmdline-turbo";
    public const string MultiPar = "multipar";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static bool Required => Environment.GetEnvironmentVariable("PAKKO_PAR2_ORACLES_REQUIRED") == "1";

    public static string ExecutablePath(string tool) => Path.Combine(OraclesRoot(), tool, tool == MultiPar ? "par2j64.exe" : "par2.exe");

    public static bool IsPresent(string tool) => File.Exists(ExecutablePath(tool));

    public static (int ExitCode, string Output) Run(string tool, string workingDirectory, params string[] arguments)
    {
        string exe = ExecutablePath(tool);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"PAR2 oracle missing: run scripts/Get-Par2Oracles.ps1 ({exe})", exe);
        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{tool} {string.Join(' ', arguments)} did not finish in {Timeout}");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string OraclesRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
                return Path.Combine(dir.FullName, "artifacts", "par2-oracles");
        }
        throw new DirectoryNotFoundException("Repository root (global.json) not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// A test that runs the named PAR2 oracles. Skipped when one is missing, unless
/// <c>PAKKO_PAR2_ORACLES_REQUIRED=1</c> (set in CI), where the test runs and fails instead, so a
/// broken download cannot pass as a skip.
/// </summary>
public sealed class Par2OracleFactAttribute : FactAttribute
{
    public Par2OracleFactAttribute(params string[] tools)
    {
        string[] missing = [.. tools.Where(t => !Par2Oracle.IsPresent(t))];
        if (missing.Length > 0 && !Par2Oracle.Required)
            Skip = $"PAR2 oracle(s) {string.Join(", ", missing)} not downloaded: run scripts/Get-Par2Oracles.ps1";
    }
}
