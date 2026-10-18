using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team: one fresh folder per test under %TEMP%, and the only place a test lets pakko
/// write. Every path a test hands pakko goes through <see cref="Inside"/>, which refuses anything
/// outside the root, and every link it makes points inside the root too, so even a pakko that
/// follows a link stays in it. Junctions are removed without recursion before the folder is.
/// </summary>
internal sealed class AdversarialScratch : IDisposable
{
    private const string Cmd = @"C:\Windows\System32\cmd.exe";
    private const string Icacls = @"C:\Windows\System32\icacls.exe";
    private readonly List<string> _links = [];
    private readonly List<string> _denied = [];

    public AdversarialScratch()
    {
        Root = Directory.CreateTempSubdirectory("pakko-redteam-").FullName;
    }

    public string Root { get; }

    /// <summary>A path under the root; fails the test for one that would leave it.</summary>
    public string Inside(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        full.Should().StartWith(Root + Path.DirectorySeparatorChar, "a red-team test never points pakko outside its scratch folder");
        return full;
    }

    public string WriteFile(string relative, string content)
    {
        string path = Inside(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Folder(string relative) => Directory.CreateDirectory(Inside(relative)).FullName;

    /// <summary>A ZIP whose entry names are written exactly as given, hostile ones included.</summary>
    public string Zip(string relative, params (string Name, string Content)[] entries)
    {
        string path = Inside(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return path;
    }

    public void Junction(string linkRelative, string targetRelative)
    {
        string link = Inside(linkRelative);
        RunCmd("mklink", "/J", link, Inside(targetRelative));
        _links.Add(link);
    }

    public void HardLink(string linkRelative, string targetRelative) =>
        RunCmd("mklink", "/H", Inside(linkRelative), Inside(targetRelative));

    /// <summary>Denies the current user <paramref name="rights"/> (icacls syntax, e.g. "R", "D",
    /// "RD") on a path under the root; undone before the folder is removed.</summary>
    public void Deny(string relative, string rights)
    {
        string path = Inside(relative);
        Run(Icacls, path, "/deny", Environment.UserName + ":(" + rights + ")");
        _denied.Add(path);
    }

    public (int ExitCode, string StdOut, string StdErr) Pakko(params string[] args) =>
        CliProcessRunner.RunIn(Root, args);

    /// <summary>pakko run with its current folder at a folder under the root.</summary>
    public (int ExitCode, string StdOut, string StdErr) PakkoIn(string relativeFolder, params string[] args) =>
        CliProcessRunner.RunIn(Inside(relativeFolder), args);

    /// <summary>Every file under the root as its relative path, read through \\?\ so names Win32 would trim show as they are.</summary>
    public IReadOnlyList<string> FilesUnder(string relative)
    {
        string longRoot = @"\\?\" + Inside(relative);
        if (!Directory.Exists(longRoot))
            return [];
        return [.. Directory.EnumerateFiles(longRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(longRoot, path))
            .Order(StringComparer.Ordinal)];
    }

    public void Dispose()
    {
        foreach (string denied in _denied)
        {
            try { Run(Icacls, denied, "/remove:d", Environment.UserName); } catch (Exception ex) when (ex is not OutOfMemoryException) { /* best-effort: the delete below reports what is left */ }
        }
        foreach (string link in _links)
        {
            try { Directory.Delete(link); } catch (IOException) { /* best-effort: the recursive delete below retries */ }
        }
        // tar.exe's sandbox can hold a file for a moment after pakko exits (TestTempFolder, T-F300).
        for (int attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                Directory.Delete(@"\\?\" + Root, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 30)
            {
                Task.Delay(100).GetAwaiter().GetResult();
            }
        }
    }

    private static void RunCmd(params string[] args) => Run(Cmd, ["/c", .. args]);

    private static void Run(string exe, params string[] args)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException(exe + " did not start");
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"{exe} {string.Join(' ', args)} failed: {error}");
    }
}

/// <summary>A test that needs File.CreateSymbolicLink (Developer Mode or the privilege); skipped without it.</summary>
// T-F419: the temp folder through \\localhost\<drive>$ - skipped, not passed, where the
// administrative share is not reachable.
public sealed class RequiresAdminShareFactAttribute : FactAttribute
{
    public RequiresAdminShareFactAttribute()
    {
        string temp = Path.GetTempPath();
        if (!Directory.Exists(@"\\localhost\" + temp[0] + "$" + temp[2..]))
            Skip = "the administrative share \\\\localhost\\" + temp[0] + "$ is not reachable on this machine";
    }
}

public sealed class RequiresSymlinkFactAttribute : FactAttribute
{
    public RequiresSymlinkFactAttribute()
    {
        string probe = Path.Combine(Path.GetTempPath(), "pakko-redteam-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.CreateSymbolicLink(probe, Path.GetTempPath());
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip = "this account cannot create symbolic links (no Developer Mode or SeCreateSymbolicLinkPrivilege)";
        }
    }
}
