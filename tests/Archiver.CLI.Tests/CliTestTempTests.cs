using System.Diagnostics;
using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F422: the CLI tests' scratch folders reached 37 GB in %TEMP%, nothing removed them.
public sealed class CliTestTempTests : IDisposable
{
    private readonly string _parent = Path.Combine(CliTestTemp.Root, "sweep-" + Guid.NewGuid().ToString("N"));

    public CliTestTempTests() => Directory.CreateDirectory(_parent);

    public void Dispose() => CliTestTemp.Remove(_parent);

    [Fact]
    public void CreateScratchDir_IsUnderThisRunsRoot()
    {
        CliFixtureFiles.CreateScratchDir().Should().StartWith(CliTestTemp.Root + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Fixtures_AreUnderThisRunsRoot()
    {
        CliFixtureFiles.ValidZip.Should().StartWith(CliTestTemp.Root + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Root_IsHeldByThisRun()
    {
        Directory.EnumerateFileSystemEntries(CliTestTemp.Parent).Should().NotBeEmpty();
        CliTestTemp.SweepAbandoned(CliTestTemp.Parent);

        Directory.Exists(CliTestTemp.Root).Should().BeTrue();
    }

    [Fact]
    public void Sweep_RemovesARootWhoseRunIsGone()
    {
        string root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".owner"), "");
        File.WriteAllText(Path.Combine(root, "left.bin"), "output of a killed run");

        CliTestTemp.SweepAbandoned(_parent);

        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void Sweep_KeepsARootWhoseRunIsAlive()
    {
        string root = NewRoot();
        using (new FileStream(Path.Combine(root, ".owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            CliTestTemp.SweepAbandoned(_parent);

            File.Exists(Path.Combine(root, ".owner")).Should().BeTrue();
        }
    }

    [Fact]
    public void Sweep_KeepsAFreshRootThatHasNoOwnerYet()
    {
        string root = NewRoot();

        CliTestTemp.SweepAbandoned(_parent);

        Directory.Exists(root).Should().BeTrue();
    }

    [Fact]
    public void Sweep_RemovesAnOldRootThatNeverGotAnOwner()
    {
        string root = NewRoot();
        Directory.SetCreationTimeUtc(root, DateTime.UtcNow.AddHours(-1));

        CliTestTemp.SweepAbandoned(_parent);

        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void Remove_RemovesAJunctionWithoutFollowingIt()
    {
        string outside = Path.Combine(_parent, "outside");
        Directory.CreateDirectory(outside);
        string kept = Path.Combine(outside, "kept.txt");
        File.WriteAllText(kept, "not the run's");
        string root = NewRoot();
        Mklink("/J", Path.Combine(root, "link"), outside);

        CliTestTemp.Remove(root);

        Directory.Exists(root).Should().BeFalse();
        File.ReadAllText(kept).Should().Be("not the run's");
    }

    [Fact]
    public void Remove_MissingFolder_DoesNothing()
    {
        FluentActions.Invoking(() => CliTestTemp.Remove(Path.Combine(_parent, "missing"))).Should().NotThrow();
    }

    private string NewRoot() => Directory.CreateDirectory(Path.Combine(_parent, Guid.NewGuid().ToString("N"))).FullName;

    private static void Mklink(string kind, string link, string target)
    {
        var start = new ProcessStartInfo(@"C:\Windows\System32\cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "/c", "mklink", kind, link, target })
            start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "mklink {0} {1}", kind, link);
    }
}
