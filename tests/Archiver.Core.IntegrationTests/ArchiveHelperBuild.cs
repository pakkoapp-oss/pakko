using System.Diagnostics;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: the Release x64 Archiver.ArchiveHelper.exe from this checkout. `dotnet test` cannot
/// build a .vcxproj, so a missing or stale exe (older than its sources) is built here through
/// msbuild found by vswhere, once per checkout across test processes. CI builds it in a step
/// before the tests, so this only builds on a developer's machine.
/// </summary>
internal static class ArchiveHelperBuild
{
    private static readonly Lazy<string> Built = new(BuildIfStale, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string ExePath => Built.Value;

    public static string RepoRoot
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("No windows-archiver-wrapper.sln above " + AppContext.BaseDirectory);
        }
    }

    private static string BuildIfStale()
    {
        string root = RepoRoot;
        string project = Path.Combine(root, "src", "Archiver.ArchiveHelper", "Archiver.ArchiveHelper.vcxproj");
        string exe = Path.Combine(root, "src", "Archiver.ArchiveHelper", "bin", "x64", "Release", "Archiver.ArchiveHelper.exe");

        using var mutex = new Mutex(false, @"Local\Pakko.ArchiveHelperBuild");
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died mid-build; the staleness check below decides again.
        }
        try
        {
            if (IsFresh(exe, Path.GetDirectoryName(project)!))
                return exe;
            RunMsBuild(project, root);
            if (!IsFresh(exe, Path.GetDirectoryName(project)!))
                throw new InvalidOperationException("msbuild succeeded but " + exe + " is still missing or stale");
            return exe;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool IsFresh(string exe, string sourceDir)
    {
        if (!File.Exists(exe))
            return false;
        DateTime built = File.GetLastWriteTimeUtc(exe);
        return Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".c", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase))
            .All(f => File.GetLastWriteTimeUtc(f) <= built);
    }

    private static void RunMsBuild(string project, string root)
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        string msbuild = Run(vswhere, ["-latest", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\Current\Bin\MSBuild.exe"],
            TimeSpan.FromMinutes(1)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? throw new InvalidOperationException("vswhere found no MSBuild; build Archiver.ArchiveHelper.vcxproj first");
        string log = Path.Combine(Path.GetTempPath(), "Pakko.ArchiveHelperBuild." + Environment.ProcessId + ".log");
        try
        {
            Run(msbuild, [project, "/p:Configuration=Release", "/p:Platform=x64", "/p:SolutionDir=" + root + Path.DirectorySeparatorChar,
                "/nologo", "/noconsolelogger", "/flp:LogFile=" + log + ";Verbosity=minimal", "/nodeReuse:false"], TimeSpan.FromMinutes(5));
        }
        catch (InvalidOperationException e) when (File.Exists(log))
        {
            throw new InvalidOperationException(e.Message + Environment.NewLine + File.ReadAllText(log), e);
        }
        finally
        {
            try { File.Delete(log); } catch (IOException) { /* best-effort: a leftover temp log is harmless */ }
        }
    }

    private static string Run(string exe, string[] arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in arguments)
            start.ArgumentList.Add(a);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + exe);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(exe + " did not finish within " + timeout);
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{exe} exited {process.ExitCode}: {stdout.Result}{stderr.Result}");
        return stdout.Result;
    }
}
