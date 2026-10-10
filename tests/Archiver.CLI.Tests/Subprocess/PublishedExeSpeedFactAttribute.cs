namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// A timing test of the published Native AOT pakko.exe against par2cmdline (T-F375). It runs only
/// when <c>PAKKO_CLI_EXE</c> names the exe under test: the default run starts the Debug JIT build,
/// whose speed says nothing about the AOT one. par2cmdline comes from
/// <c>scripts/Get-Par2Oracles.ps1</c>; without it the test is skipped, except under
/// <c>PAKKO_PAR2_ORACLES_REQUIRED=1</c> (set in CI), where it runs and fails.
/// </summary>
public sealed class PublishedExeSpeedFactAttribute : FactAttribute
{
    public PublishedExeSpeedFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PAKKO_CLI_EXE")))
            Skip = "PAKKO_CLI_EXE is not set: the default run starts the JIT build, not the published exe";
        else if (!File.Exists(Par2CmdLinePath) && Environment.GetEnvironmentVariable("PAKKO_PAR2_ORACLES_REQUIRED") != "1")
            Skip = "par2cmdline not downloaded: run scripts/Get-Par2Oracles.ps1";
    }

    public static string Par2CmdLinePath
    {
        get
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
                    return Path.Combine(dir.FullName, "artifacts", "par2-oracles", "par2cmdline", "par2.exe");
            }

            throw new InvalidOperationException($"Repository root not found above {AppContext.BaseDirectory}");
        }
    }
}
