using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F419 red team for "Delete after": SourceRecycler over the real Win32 operations, driven either
// directly or by what Core's FullyProcessedSources reports. Rule under test: never remove a byte
// that is not safely in the archive, never follow a link to delete what is behind it, never delete
// permanently without the confirmation. Every destructive case runs in its own scratch folder and
// goes through the permanent-delete path, so nothing lands in the user's real Recycle Bin — except
// the one junction-recycle test, which purges its own records.
public sealed class SourceRecyclerAdversarialTests : IDisposable
{
    // The real resolution, existence check and permanent delete; the volume is always reported
    // "not fixed" so the recycler takes its permanent path, and a Recycle Bin call fails the test.
    private sealed class PermanentOnlyOps : ISourceDeleteOperations
    {
        private readonly Win32SourceDeleteOperations _real = new(() => IntPtr.Zero);
        public readonly List<string> PermanentlyDeleted = [];

        public string? ResolveFinalPath(string path) => _real.ResolveFinalPath(path);
        public bool IsOnFixedLocalVolume(string finalPath) => false;
        public void MoveToRecycleBin(IReadOnlyList<string> finalPaths) =>
            throw new InvalidOperationException("this test must not reach the real Recycle Bin");
        public void DeletePermanently(string finalPath)
        {
            PermanentlyDeleted.Add(finalPath);
            _real.DeletePermanently(finalPath);
        }
        public bool Exists(string path) => _real.Exists(path);
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pakko-tf419-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];
    private readonly PermanentOnlyOps _ops = new();
    private readonly List<IReadOnlyList<string>> _confirmCalls = [];

    public SourceRecyclerAdversarialTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (string junction in _junctions)
        {
            try
            {
                if (Directory.Exists(junction))
                    Directory.Delete(junction, recursive: false);
            }
            catch (IOException) { /* best-effort: the scratch delete below reports what is left */ }
        }
        // .NET's recursive delete removes a link it meets without entering it.
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Scratch(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(_root, relative));
        path.Should().StartWith(_root + Path.DirectorySeparatorChar, "every destructive case stays in its scratch folder");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteFile(string folder, string name, string content)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Junction(string junctionPath, string target)
    {
        junctionPath.Should().StartWith(_root);
        DirectoryJunction.Create(junctionPath, target);
        _junctions.Add(junctionPath);
        File.GetAttributes(junctionPath).HasFlag(FileAttributes.ReparsePoint).Should().BeTrue("precondition: a junction");
        return junctionPath;
    }

    private Func<IReadOnlyList<string>, Task<bool>> Confirm(bool answer) => paths =>
    {
        _confirmCalls.Add(paths);
        return Task.FromResult(answer);
    };

    // --- Links: never delete what is behind one ---

    [Fact]
    public async Task JunctionAsSource_PermanentDelete_TargetContentSurvives()
    {
        string target = Scratch("target");
        string precious = WriteFile(target, "precious.txt", "behind the link");
        string link = Junction(Path.Combine(_root, "link"), target);

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([link], SourceSnapshot.Take([link]), Confirm(true));

        File.Exists(precious).Should().BeTrue("deleting a junction must not delete its target's content");
        result.Deleted.Should().Contain(link);
        _confirmCalls.Should().ContainSingle();
    }

    // The one real Recycle Bin call: the junction is recycled, its own records are purged after.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void JunctionAsSource_RealRecycle_TargetContentSurvives()
    {
        string target = Scratch("target");
        string precious = WriteFile(target, "precious.txt", "behind the link");
        string unique = "pakko-tf419-link-" + Guid.NewGuid().ToString("N");
        string link = Junction(Path.Combine(_root, unique), target);
        var real = new Win32SourceDeleteOperations(() => IntPtr.Zero);
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        string bin = Path.Combine(Path.GetPathRoot(link)!, "$Recycle.Bin", sid);

        real.MoveToRecycleBin([real.ResolveFinalPath(link)!]);

        bool targetSurvived = File.Exists(precious);
        PurgeRecycleRecords(bin, unique);
        Directory.Exists(link).Should().BeFalse("precondition: the link left its folder");
        targetSurvived.Should().BeTrue("recycling a junction must not take its target's content");
    }

    private static void PurgeRecycleRecords(string bin, string name)
    {
        foreach (string info in Directory.EnumerateFiles(bin, "$I*"))
        {
            string text;
            try { text = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(info)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; } // someone else's record
            if (!text.Contains(name, StringComparison.OrdinalIgnoreCase))
                continue;
            string recycled = Path.Combine(bin, "$R" + Path.GetFileName(info)[2..]);
            if (Directory.Exists(recycled))
            {
                // A link is removed by itself, never entered.
                bool isLink = File.GetAttributes(recycled).HasFlag(FileAttributes.ReparsePoint);
                Directory.Delete(recycled, recursive: !isLink);
            }
            else if (File.Exists(recycled))
            {
                File.Delete(recycled);
            }
            File.Delete(info);
        }
    }

    [Fact]
    public void JunctionAsSource_ResolvesToTheLinkItself()
    {
        string target = Scratch("target");
        string link = Junction(Path.Combine(_root, "link"), target);

        _ops.ResolveFinalPath(link).Should().Be(link, "the link is what is deleted, never its target");
    }

    [Fact]
    public async Task FolderHoldingJunctionToOutside_PermanentDelete_OutsideContentSurvives()
    {
        string outside = Scratch("outside");
        string precious = WriteFile(outside, "precious.txt", "outside the source");
        string source = Scratch("source");
        string mine = WriteFile(source, "mine.txt", "archived");
        Junction(Path.Combine(source, "link"), outside);

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([source], SourceSnapshot.Take([source]), Confirm(true));

        File.Exists(precious).Should().BeTrue("a junction inside the deleted folder must not be followed");
        // Either the source is gone, or it is reported "not deleted" and left as it was — a half
        // deleted folder reported "not deleted" tells the user something false.
        if (result.NotDeleted.Contains(source))
            File.Exists(mine).Should().BeTrue($"{source} is reported not deleted, yet its content was deleted");
        else
            result.Deleted.Should().Contain(source);
    }

    [Fact]
    public async Task SourceSpelledThroughJunction_OnlyTheSourcesOwnBytesDeleted()
    {
        // link\sub and target\sub are the same bytes, so deleting them is the user's request; the
        // link must not widen the delete to anything else behind it.
        string target = Scratch("target");
        string sibling = WriteFile(target, "other.txt", "next to the source, behind the link");
        string sub = Scratch(@"target\sub");
        WriteFile(sub, "file.txt", "x");
        string link = Junction(Path.Combine(_root, "link"), target);
        string viaLink = Path.Combine(link, "sub");

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([viaLink], SourceSnapshot.Take([viaLink]), Confirm(true));

        File.Exists(sibling).Should().BeTrue("only the source itself may go");
        Directory.Exists(target).Should().BeTrue("the link's target folder is not the source");
        result.Deleted.Should().Equal(viaLink);
    }

    [Fact]
    public async Task SourceReplacedByJunctionWhileConfirmIsOpen_TargetContentSurvives()
    {
        string source = Scratch("source");
        WriteFile(source, "archived.txt", "in the archive");
        string elsewhere = Scratch("elsewhere");
        string precious = WriteFile(elsewhere, "precious.txt", "never archived");

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([source], SourceSnapshot.Take([source]), paths =>
        {
            // While the dialog is open the folder is moved away and a junction takes its name.
            Directory.Move(source, Path.Combine(_root, "moved"));
            Junction(source, elsewhere);
            return Task.FromResult(true);
        });

        File.Exists(precious).Should().BeTrue("the delete must not reach through a link that appeared after the run");
        result.Should().NotBeNull();
    }

    // --- Bytes that are not in the archive ---

    [Fact]
    public async Task EndToEnd_FileAddedToSourceAfterArchiving_IsNotDeleted()
    {
        string source = Scratch("source");
        WriteFile(source, "archived.txt", "in the archive");
        ISourceSnapshot snapshot = SourceSnapshot.Take([source]);
        ArchiveResult archived = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Scratch("dest"),
        });
        archived.FullyProcessedSources.Should().Equal([source], "precondition: Core calls the source fully processed");

        // The App awaits the summary dialog before cleanup; the user keeps working meanwhile.
        string late = WriteFile(source, "written-after.txt", "never archived");

        await new SourceRecycler(_ops).DeleteAsync(archived.FullyProcessedSources, snapshot, Confirm(true));

        File.Exists(late).Should().BeTrue("written-after.txt is not in the archive, so deleting it loses it");
    }

    [Fact]
    public async Task EndToEnd_FileChangedInSourceAfterArchiving_NewContentNotDeleted()
    {
        string source = Scratch("source");
        string file = WriteFile(source, "doc.txt", "version 1");
        ISourceSnapshot snapshot = SourceSnapshot.Take([file]);
        ArchiveResult archived = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = Scratch("dest"),
        });
        archived.FullyProcessedSources.Should().Equal([file], "precondition");
        File.WriteAllText(file, "version 2 - edited while the summary was open");

        await new SourceRecycler(_ops).DeleteAsync(archived.FullyProcessedSources, snapshot, Confirm(true));

        File.Exists(file).Should().BeTrue("version 2 is not in the archive");
    }

    [Fact]
    public async Task EndToEnd_ArchiveWrittenIntoSourceThroughJunction_ArchiveSurvivesDelete()
    {
        string source = Scratch("source");
        WriteFile(source, "data.txt", "data");
        string link = Junction(Path.Combine(_root, "link"), source);
        ISourceSnapshot snapshot = SourceSnapshot.Take([source]);
        ArchiveResult archived = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = link,
            ArchiveName = "backup",
        });
        string zip = Path.Combine(source, "backup.zip");
        File.Exists(zip).Should().BeTrue("precondition: the archive sits inside the source");

        await new SourceRecycler(_ops).DeleteAsync(archived.FullyProcessedSources, snapshot, Confirm(true));

        File.Exists(zip).Should().BeTrue("the only copy of the data is this archive; deleting the source took it too");
    }

    [SubstDriveFact]
    public async Task EndToEnd_SubstDriveRoot_HiddenSystemFileNotArchived_IsNotDeleted()
    {
        string backing = Scratch("backing");
        WriteFile(backing, "normal.txt", "archived");
        string hidden = WriteFile(backing, "hidden-system.dat", "left out at a drive root");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        string driveRoot = SubstDrive.Map(backing);
        try
        {
            ISourceSnapshot snapshot = SourceSnapshot.Take([driveRoot]);
            ArchiveResult archived = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [driveRoot],
                DestinationFolder = Scratch("dest"),
                ArchiveName = "drive",
            });

            await new SourceRecycler(_ops).DeleteAsync(archived.FullyProcessedSources, snapshot, Confirm(true));
        }
        finally
        {
            SubstDrive.Unmap(driveRoot);
        }

        File.Exists(hidden).Should().BeTrue("hidden-system.dat was never archived");
    }

    // --- The snapshot must not stop what may go (T-F419 triage) ---

    // A key spelled unlike Core's would keep every source silently; an unchanged one goes.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndToEnd_UnchangedSource_IsDeleted(bool trailingSeparator)
    {
        string source = Scratch("source");
        WriteFile(source, "archived.txt", "in the archive");
        string given = trailingSeparator ? source + Path.DirectorySeparatorChar : source;
        ISourceSnapshot snapshot = SourceSnapshot.Take([given]);
        ArchiveResult archived = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [given],
            DestinationFolder = Scratch("dest"),
        });

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync(archived.FullyProcessedSources, snapshot, Confirm(true));

        Directory.Exists(source).Should().BeFalse("nothing changed since it was archived");
        result.NotDeleted.Should().BeEmpty();
    }

    [Fact]
    public async Task EndToEnd_ExtractedArchive_Unchanged_IsDeleted()
    {
        string source = Scratch("source");
        WriteFile(source, "a.txt", "a");
        string dest = Scratch("dest");
        await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = dest,
            ArchiveName = "a",
        });
        string zip = Path.Combine(dest, "a.zip");
        ISourceSnapshot snapshot = SourceSnapshot.Take([zip]);
        ArchiveResult extracted = await new ZipArchiveService(new GroupPolicyOptions()).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = Scratch("out"),
        });

        await new SourceRecycler(_ops).DeleteAsync(extracted.FullyProcessedSources, snapshot, Confirm(true));

        File.Exists(zip).Should().BeFalse("the archive is wholly on disk and unchanged");
    }

    [Fact]
    public async Task SourceNotInTheSnapshot_IsKeptAndReported()
    {
        string source = Scratch("source");
        WriteFile(source, "a.txt", "a");

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([source], SourceSnapshot.Take([]), Confirm(true));

        Directory.Exists(source).Should().BeTrue();
        result.NotDeleted.Should().Equal(source);
    }

    // --- Spellings, duplicates, roots (fake ops: decisions only) ---

    private sealed class RecordingOps : ISourceDeleteOperations
    {
        public readonly Dictionary<string, string> Final = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> OnDisk = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Touched = [];
        public bool Fixed = true;

        public string? ResolveFinalPath(string path) => Final.GetValueOrDefault(path);
        public bool IsOnFixedLocalVolume(string finalPath) => Fixed;
        public void MoveToRecycleBin(IReadOnlyList<string> finalPaths)
        {
            Touched.AddRange(finalPaths);
            foreach (string p in finalPaths) OnDisk.Remove(p);
        }
        public void DeletePermanently(string finalPath)
        {
            Touched.Add(finalPath);
            OnDisk.Remove(finalPath);
        }
        public bool Exists(string path) => OnDisk.Contains(path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceResolvingToVolumeRoot_NeverDeleted(bool isFixed)
    {
        // A SUBST or mapped drive root, or a source the user typed as "C:\", resolves to a volume root.
        var ops = new RecordingOps { Fixed = isFixed };
        ops.Final[@"P:\"] = @"C:\";
        ops.OnDisk.Add(@"C:\");

        await new SourceRecycler(ops).DeleteAsync([@"P:\"], AllUnchangedSnapshot.Instance, Confirm(true));

        ops.Touched.Should().NotContain(@"C:\", "a whole volume root is never handed to the Recycle Bin or deleted");
    }

    [Fact]
    public async Task TwoSpellingsOfOnePath_HandedOverOnce()
    {
        var ops = new RecordingOps();
        ops.Final[@"C:\work\LONGFO~1"] = @"C:\work\Long folder";
        ops.Final[@"C:\work\Long folder"] = @"C:\work\Long folder";
        ops.OnDisk.Add(@"C:\work\Long folder");

        await new SourceRecycler(ops).DeleteAsync([@"C:\work\LONGFO~1", @"C:\work\Long folder"], AllUnchangedSnapshot.Instance, Confirm(true));

        ops.Touched.Should().OnlyHaveUniqueItems("one item on disk is one entry in the delete list");
    }

    [Fact]
    public async Task NonFixed_ConfirmationNamesEveryPathThatIsThenDeleted()
    {
        var ops = new RecordingOps { Fixed = false };
        ops.Final[@"Z:\a"] = @"\\server\share\a";
        ops.Final[@"Z:\b"] = @"\\server\share\b";
        ops.OnDisk.Add(@"\\server\share\a");
        ops.OnDisk.Add(@"\\server\share\b");

        await new SourceRecycler(ops).DeleteAsync([@"Z:\a", @"Z:\b"], AllUnchangedSnapshot.Instance, Confirm(true));

        _confirmCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(@"Z:\a", @"Z:\b");
        ops.Touched.Should().BeEquivalentTo(@"\\server\share\a", @"\\server\share\b");
    }

    [Fact]
    public async Task ConfirmationThrows_NothingDeleted()
    {
        var ops = new RecordingOps { Fixed = false };
        ops.Final[@"Z:\a"] = @"\\server\share\a";
        ops.OnDisk.Add(@"\\server\share\a");

        Func<Task> act = () => new SourceRecycler(ops).DeleteAsync([@"Z:\a"], AllUnchangedSnapshot.Instance, _ => throw new InvalidOperationException("dialog failed"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        ops.Touched.Should().BeEmpty("no confirmation, no permanent delete");
    }

    // --- Real Win32 resolution of odd spellings ---

    [Fact]
    public void ExtendedLengthSpelling_ResolvesToSameFinalPath()
    {
        string folder = Scratch("plain");

        _ops.ResolveFinalPath(@"\\?\" + folder).Should().Be(_ops.ResolveFinalPath(folder));
    }

    [Fact]
    public async Task ReadOnlyFileInSource_PermanentDelete_ReportedNotDeleted_NotSilentlyDeleted()
    {
        string source = Scratch("source");
        string ro = WriteFile(source, "ro.txt", "read only");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        try
        {
            RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([source], SourceSnapshot.Take([source]), Confirm(true));

            (result.Deleted.Contains(source) ^ result.NotDeleted.Contains(source)).Should().BeTrue("the source is in exactly one list");
            if (result.Deleted.Contains(source))
                Directory.Exists(source).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(ro))
                File.SetAttributes(ro, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task EndToEnd_TrailingDotSource_DeletesOnlyWhatWasArchived()
    {
        // "a." and "a" are one name to Win32 but two files to NTFS. Whatever file Core read for a
        // source spelled "a.", the recycler must delete that same file and no other.
        string folder = Scratch("dots");
        string plain = WriteFile(folder, "a", "plain a");
        string dotted = Path.Combine(folder, "a.");
        File.WriteAllText(@"\\?\" + dotted, "the dotted one");
        File.Exists(@"\\?\" + dotted).Should().BeTrue("precondition: a real 'a.' file exists");
        string dest = Scratch("dest");
        try
        {
            ISourceSnapshot snapshot = SourceSnapshot.Take([dotted]);
            ArchiveResult archived = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [dotted],
                DestinationFolder = dest,
                ArchiveName = "dots",
            });
            string archivedText;
            using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(dest, "dots.zip")))
            using (StreamReader reader = new(zip.Entries.Single().Open()))
                archivedText = reader.ReadToEnd();

            await new SourceRecycler(_ops).DeleteAsync(archived.FullyProcessedSources, snapshot, Confirm(true));

            if (!File.Exists(plain))
                archivedText.Should().Be("plain a", "'a' was deleted, so 'a' must be what the archive holds");
            if (!File.Exists(@"\\?\" + dotted))
                archivedText.Should().Be("the dotted one", "'a.' was deleted, so 'a.' must be what the archive holds");
        }
        finally
        {
            if (File.Exists(@"\\?\" + dotted))
                File.Delete(@"\\?\" + dotted);
        }
    }
}

/// <summary>SUBST of a free drive letter onto a scratch folder; the letter is removed again in Unmap.</summary>
internal static class SubstDrive
{
    private const string Exe = @"C:\Windows\System32\subst.exe";

    public static string? FreeLetter() => FreeLetters().FirstOrDefault();

    private static IEnumerable<string> FreeLetters()
    {
        HashSet<char> used = [.. DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0]))];
        for (char c = 'P'; c <= 'Y'; c++)
            if (!used.Contains(c))
                yield return c + ":";
    }

    public static string Map(string folder)
    {
        // T-F419 triage: another test project maps one at the same time; a letter taken between
        // the look and the mapping makes subst fail, and the next free one is tried.
        string? letter = null;
        foreach (string candidate in FreeLetters())
        {
            try
            {
                Run(candidate + " \"" + folder + "\"");
                letter = candidate;
                break;
            }
            catch (InvalidOperationException)
            {
                // taken meanwhile: the next one
            }
        }
        if (letter is null)
            throw new InvalidOperationException("no free drive letter");
        string root = letter + @"\";
        if (!Directory.Exists(root))
        {
            Unmap(root);
            throw new InvalidOperationException("precondition: the SUBST drive is not reachable");
        }
        return root;
    }

    public static void Unmap(string root) => Run(root[..2] + " /D");

    private static void Run(string arguments)
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("subst did not start");
        if (!process.WaitForExit(10_000))
            throw new TimeoutException("subst " + arguments);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"subst {arguments} exited {process.ExitCode}");
    }
}

/// <summary>A test that maps a SUBST drive; skipped without subst.exe or a free letter.</summary>
public sealed class SubstDriveFactAttribute : FactAttribute
{
    public SubstDriveFactAttribute()
    {
        if (!File.Exists(@"C:\Windows\System32\subst.exe"))
            Skip = "subst.exe not present";
        else if (SubstDrive.FreeLetter() is null)
            Skip = "no free drive letter P..Y";
    }
}
