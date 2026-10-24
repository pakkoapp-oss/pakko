namespace Archiver.CLI.Tests;

/// <summary>
/// T-F422: every scratch folder of a test run lives under one root, %TEMP%\Archiver.CLI.Tests\&lt;guid&gt;,
/// removed when the run ends. The run holds its root's <c>.owner</c> file open without sharing, so a
/// later run can tell a root whose run is gone (killed, crashed) from a live one and removes it. A
/// removal never follows a link: <see cref="Directory.Delete(string, bool)"/> removes a junction or
/// symlink itself, not what it points to.
/// </summary>
internal static class CliTestTemp
{
    private const string OwnerFileName = ".owner";

    // A root younger than this without its .owner file may be a run that has not opened it yet.
    private static readonly TimeSpan OwnerGrace = TimeSpan.FromMinutes(5);

    private static readonly Lazy<(string Root, FileStream Owner)> Run = new(Start);

    public static string Parent { get; } = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Tests");

    public static string Root => Run.Value.Root;

    public static string NewDir()
    {
        string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Removes every root under <paramref name="parent"/> whose run is gone.</summary>
    public static void SweepAbandoned(string parent)
    {
        if (!Directory.Exists(parent))
            return;
        foreach (string root in Directory.EnumerateDirectories(parent))
        {
            if (IsAbandoned(root))
                Remove(root);
        }
    }

    /// <summary>Removes a folder and what is under it; a link inside is removed, never followed.
    /// Best-effort: tar.exe's sandbox can hold a file for a moment after pakko exits (T-F300).</summary>
    public static void Remove(string path)
    {
        for (int attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                Directory.Delete(@"\\?\" + path, recursive: true);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // best-effort: the next run's sweep retries it
            }
        }
    }

    private static bool IsAbandoned(string root)
    {
        string owner = Path.Combine(root, OwnerFileName);
        if (!File.Exists(owner))
            return DateTime.UtcNow - Directory.GetCreationTimeUtc(root) > OwnerGrace;
        try
        {
            File.Delete(owner);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // a live run holds it open
        }
    }

    private static (string Root, FileStream Owner) Start()
    {
        SweepAbandoned(Parent);
        string root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var owner = new FileStream(Path.Combine(root, OwnerFileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            owner.Dispose();
            Remove(root);
        };
        return (root, owner);
    }
}
