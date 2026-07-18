using System.Diagnostics;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// Launches the real, built pakko.exe (Archiver.CLI project, AssemblyName renamed T-F116
/// follow-up) as a subprocess and captures its exit code and stdout/stderr — the new test layer
/// TASKS.md's T-F09 acceptance criteria calls for, since a human or script types this exe's
/// arguments directly, so its exit code/stdout text IS the public contract (unlike
/// Archiver.Shell, whose args are always generated programmatically).
/// Deliberately plain System.Diagnostics.Process, not Archiver.Core's internal
/// SandboxedProcessLauncher — that machinery sandboxes untrusted external binaries (tar.exe,
/// vendored 7za.exe) via Job Objects/AppContainer; pakko.exe is a trusted, first-party sibling
/// build artifact, not something that needs containing.
/// </summary>
internal static class CliProcessRunner
{
    public static string ExePath { get; } = Resolve();

    public static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo(ExePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {ExePath}");

        string stdOut = process.StandardOutput.ReadToEnd();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdOut, stdErr);
    }

    /// <summary>
    /// Runs pakko.exe with raw bytes piped to stdin and captures raw bytes from stdout —
    /// needed for T-F116's -si/-so byte round-trip assertions, where a text-based StdOut string
    /// capture would corrupt or mask the exact byte comparison being tested. Reads stdout/stderr
    /// on background tasks while writing stdin, to avoid the classic deadlock where a large
    /// enough payload fills the OS pipe buffer before the child has drained it.
    /// </summary>
    public static (int ExitCode, byte[] StdOut, string StdErr) RunWithBinaryStdio(byte[] stdinBytes, params string[] args)
    {
        var startInfo = new ProcessStartInfo(ExePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {ExePath}");

        using var stdOutBuffer = new MemoryStream();
        Task stdOutTask = process.StandardOutput.BaseStream.CopyToAsync(stdOutBuffer);
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

        process.StandardInput.BaseStream.Write(stdinBytes, 0, stdinBytes.Length);
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.Close();

        stdOutTask.Wait();
        string stdErr = stdErrTask.GetAwaiter().GetResult();
        process.WaitForExit();

        return (process.ExitCode, stdOutBuffer.ToArray(), stdErr);
    }

    // AppContext.BaseDirectory for this test assembly ends in ...\bin\<Configuration>\<TFM>\ —
    // Archiver.CLI shares TargetFramework=net8.0 and is built with the same Configuration, so
    // mirroring those same two trailing segments onto its own bin output is robust to Debug vs.
    // Release without hardcoding either.
    private static string Resolve()
    {
        var testBinDir = new DirectoryInfo(AppContext.BaseDirectory);
        string tfm = testBinDir.Name;
        string configuration = testBinDir.Parent?.Name
            ?? throw new InvalidOperationException($"Could not determine Configuration from {AppContext.BaseDirectory}");

        DirectoryInfo? repoRoot = testBinDir;
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot.FullName, "windows-archiver-wrapper.sln")))
            repoRoot = repoRoot.Parent;
        if (repoRoot is null)
            throw new InvalidOperationException($"Could not locate windows-archiver-wrapper.sln above {AppContext.BaseDirectory}");

        string exePath = Path.Combine(repoRoot.FullName, "src", "Archiver.CLI", "bin", configuration, tfm, "pakko.exe");
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"pakko.exe not found at {exePath} — build src/Archiver.CLI first.");

        return exePath;
    }
}
