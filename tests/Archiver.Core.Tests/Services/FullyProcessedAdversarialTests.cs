using System.IO.Compression;
using System.Runtime.InteropServices;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F419 red team: ArchiveResult.FullyProcessedSources is the only gate the App's "Delete after"
// consults. Rule under test: a source is listed only when every byte of it is safely in the
// archive (creation), or every entry of the archive is on disk (extraction) — and an archive that
// lands inside its own source never makes that source deletable, whatever spelling reaches it.
public sealed class FullyProcessedAdversarialTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();
    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        // Links first, non-recursively, so the scratch cleanup can never walk through one.
        foreach (string junction in _junctions)
        {
            try { Directory.Delete(junction, recursive: false); } catch (IOException) { /* best-effort: TempDirectory retries */ }
        }
        _temp.Dispose();
    }

    private string Scratch(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        path.Should().StartWith(_temp.Path, "every destructive case stays in its scratch folder");
        Directory.CreateDirectory(path);
        return path;
    }

    private string Junction(string junctionPath, string target)
    {
        DirectoryJunction.Create(junctionPath, target);
        _junctions.Add(junctionPath);
        File.GetAttributes(junctionPath).HasFlag(FileAttributes.ReparsePoint).Should().BeTrue();
        return junctionPath;
    }

    private string SourceFolder(string name)
    {
        string src = Scratch(name);
        File.WriteAllText(Path.Combine(src, "keep.txt"), "precious");
        return src;
    }

    // --- Creation: the archive written inside its own source under another spelling ---

    [Fact]
    public async Task Archive_Single_DestinationIsJunctionIntoSource_SourceNotDeletable()
    {
        string src = SourceFolder("src");
        string viaLink = Junction(Path.Combine(_temp.Path, "j"), src);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = viaLink,
            ArchiveName = "out",
        });

        File.Exists(Path.Combine(src, "out.zip")).Should().BeTrue("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().NotContain(src,
            "deleting the source would delete the archive just written into it (reached through a junction)");
    }

    [Fact]
    public async Task Archive_Separate_DestinationIsJunctionIntoSource_SourceNotDeletable()
    {
        string src = SourceFolder("src");
        string viaLink = Junction(Path.Combine(_temp.Path, "j"), src);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = viaLink,
            Mode = ArchiveMode.SeparateArchives,
        });

        File.Exists(Path.Combine(src, "src.zip")).Should().BeTrue("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().NotContain(src,
            "deleting the source would delete the archive just written into it (reached through a junction)");
    }

    [Fact]
    public async Task Archive_Single_SourceNamedThroughJunction_DestinationIsRealSource_SourceNotDeletable()
    {
        string real = SourceFolder(@"real\sub");
        string viaLink = Junction(Path.Combine(_temp.Path, "j"), Path.Combine(_temp.Path, "real"));
        string source = Path.Combine(viaLink, "sub");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = real,
            ArchiveName = "out",
        });

        File.Exists(Path.Combine(real, "out.zip")).Should().BeTrue("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().BeEmpty(
            "the source (spelled through a junction) holds the archive just written");
    }

    [Fact]
    public async Task Archive_Single_DestinationIsExtendedLengthSpellingOfSource_SourceNotDeletable()
    {
        string src = SourceFolder("src");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = @"\\?\" + src,
            ArchiveName = "out",
        });

        File.Exists(Path.Combine(src, "out.zip")).Should().BeTrue("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().NotContain(src,
            @"the \\?\ spelling of the destination is the same folder as the source");
    }

    [RequiresShortNamesFact]
    public async Task Archive_Single_SourceIsShortNameOfDestination_SourceNotDeletable()
    {
        string src = SourceFolder("long source folder name");
        string shortSrc = ShortNames.Of(src);
        shortSrc.Should().NotBe(src, "precondition: an 8.3 spelling exists");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [shortSrc],
            DestinationFolder = src,
            ArchiveName = "out",
        });

        File.Exists(Path.Combine(src, "out.zip")).Should().BeTrue("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().NotContain(shortSrc,
            "the 8.3 spelling of the source is the destination folder");
    }

    [Fact]
    public async Task Archive_Single_DestinationWithTrailingDotIsSource_SourceNotDeletable()
    {
        string src = SourceFolder("src");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = src + @"\sub\..",
            ArchiveName = "out",
        });

        File.Exists(Path.Combine(src, "out.zip")).Should().BeTrue("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().NotContain(src);
    }

    // --- Creation: bytes not in the archive ---

    [Fact]
    public async Task Archive_Single_FileLockedExclusively_SourceNotDeletable()
    {
        string src = SourceFolder("src");
        string locked = Path.Combine(src, "locked.bin");
        File.WriteAllText(locked, "cannot be read");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = Scratch("dest"),
        });

        result.FullyProcessedSources.Should().NotContain(src, "locked.bin is not in the archive");
    }

    [Fact]
    public async Task Archive_Single_JunctionAsSource_NotDeletable()
    {
        string real = SourceFolder("real");
        string link = Junction(Path.Combine(_temp.Path, "j"), real);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [link],
            DestinationFolder = Scratch("dest"),
            ArchiveName = "out",
        });

        result.FullyProcessedSources.Should().NotContain(link, "a link source is not archived");
    }

    [Fact]
    public async Task Archive_Single_SourceContainsJunction_NotDeletable()
    {
        string outside = SourceFolder("outside");
        string src = SourceFolder("src");
        Junction(Path.Combine(src, "link"), outside);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = Scratch("dest"),
        });

        result.FullyProcessedSources.Should().NotContain(src, "the link inside is not archived");
    }

    [Fact]
    public async Task Archive_Single_SourceAndItsParentBoth_ArchiveInChild_NeitherDeletable()
    {
        string parent = SourceFolder("parent");
        string child = SourceFolder(@"parent\child");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [parent, child],
            DestinationFolder = child,
            ArchiveName = "out",
        });

        File.Exists(Path.Combine(child, "out.zip")).Should().BeTrue("precondition");
        result.FullyProcessedSources.Should().BeEmpty("both the child and its parent hold the new archive");
    }

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> Password =>
        _ => Task.FromResult(new PasswordDecision { Password = "correct horse" });

    [Theory]
    [InlineData(ArchiveMode.SingleArchive, true)]
    [InlineData(ArchiveMode.SeparateArchives, true)]
    [InlineData(ArchiveMode.SeparateArchives, false)]
    public async Task Archive_FileLockedExclusively_ModesAndEncryption_SourceNotDeletable(ArchiveMode mode, bool encrypted)
    {
        string src = SourceFolder("src");
        string locked = Path.Combine(src, "locked.bin");
        File.WriteAllText(locked, "cannot be read");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = Scratch("dest"),
            Mode = mode,
            ResolvePasswordAsync = encrypted ? Password : null,
        });

        result.FullyProcessedSources.Should().NotContain(src, "locked.bin is not in the archive");
    }

    [SubstDriveFact]
    public async Task Archive_DriveRootSource_HiddenSystemEntryLeftOut_SourceNotDeletable()
    {
        string backing = SourceFolder("backing");
        string hidden = Path.Combine(backing, "hidden-system.dat");
        File.WriteAllText(hidden, "not archived at a drive root");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        string root = SubstDrive.Map(backing);
        try
        {
            ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [root],
                DestinationFolder = Scratch("dest"),
                ArchiveName = "root",
            });

            using ZipArchive archive = ZipFile.OpenRead(Path.Combine(_temp.Path, "dest", "root.zip"));
            bool hiddenArchived = archive.Entries.Any(e => e.Name == "hidden-system.dat");
            if (!hiddenArchived)
                result.FullyProcessedSources.Should().NotContain(root,
                    "hidden-system.dat was left out of the archive, and the root resolves to a real folder the recycler would delete");
        }
        finally
        {
            SubstDrive.Unmap(root);
        }
    }

    // --- Extraction: entries that collide on disk, so one entry's content is not there ---

    private string WriteZip(string name, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Scratch("archives"), name);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entryName, string content) in entries)
        {
            using StreamWriter writer = new(archive.CreateEntry(entryName).Open());
            writer.Write(content);
        }
        return path;
    }

    private static List<string> AllContents(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(File.ReadAllText)]
            : [];

    [Theory]
    [InlineData("a.txt", "A.txt", ConflictBehavior.Overwrite)]
    [InlineData("a.txt", "A.txt", ConflictBehavior.Rename)]
    [InlineData("a.txt", "A.txt", ConflictBehavior.Skip)]
    [InlineData("a.txt", "a.txt.", ConflictBehavior.Overwrite)]
    [InlineData("a.txt", "a.txt ", ConflictBehavior.Overwrite)]
    [InlineData("d/a.txt", "D/a.txt", ConflictBehavior.Overwrite)]
    [InlineData("d/a.txt", @"d\a.txt", ConflictBehavior.Overwrite)]
    [InlineData("a.txt", "a.txt", ConflictBehavior.Overwrite)]
    public async Task Extract_TwoEntriesSameDiskPath_ArchiveDeletableOnlyIfBothContentsOnDisk(
        string first, string second, ConflictBehavior onConflict)
    {
        string zip = WriteZip("collide.zip", (first, "FIRST"), (second, "SECOND"));
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            OnConflict = onConflict,
        });

        List<string> contents = AllContents(dest);
        bool bothOnDisk = contents.Contains("FIRST") && contents.Contains("SECOND");
        if (!bothOnDisk)
            result.FullyProcessedSources.Should().NotContain(zip,
                $"only [{string.Join(", ", contents)}] reached the disk, so deleting the archive loses an entry");
    }

    [Fact]
    public async Task Extract_FileEntryAndFolderEntrySameName_ArchiveDeletableOnlyIfBothOnDisk()
    {
        string zip = WriteZip("shape.zip", ("x", "FILE"), ("x/y.txt", "INSIDE"));
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            OnConflict = ConflictBehavior.Overwrite,
        });

        List<string> contents = AllContents(dest);
        if (!(contents.Contains("FILE") && contents.Contains("INSIDE")))
            result.FullyProcessedSources.Should().NotContain(zip, $"only [{string.Join(", ", contents)}] reached the disk");
    }

    [Fact]
    public async Task Extract_FolderInTheWayAtDestination_ArchiveNotDeletable()
    {
        string zip = WriteZip("blocked.zip", ("a.txt", "A"), ("b.txt", "B"));
        string dest = Scratch("out");
        Directory.CreateDirectory(Path.Combine(dest, "a.txt"));

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.FullyProcessedSources.Should().NotContain(zip, "a.txt could not be written over a folder");
    }

    [Fact]
    public async Task Extract_LockedDestinationFile_Overwrite_ArchiveNotDeletable()
    {
        string zip = WriteZip("locked.zip", ("a.txt", "NEW"), ("b.txt", "B"));
        string dest = Scratch("out");
        string existing = Path.Combine(dest, "a.txt");
        File.WriteAllText(existing, "OLD");
        using var hold = new FileStream(existing, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.FullyProcessedSources.Should().NotContain(zip, "a.txt could not replace a locked file");
    }

    [Fact]
    public async Task Extract_StoredEntryWithBadCrc_ArchiveNotDeletable()
    {
        string zip = Path.Combine(Scratch("archives"), "crc.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(archive.CreateEntry("a.txt", CompressionLevel.NoCompression).Open());
            writer.Write("MARKER-CONTENT-0123456789");
        }
        byte[] bytes = File.ReadAllBytes(zip);
        int at = bytes.AsSpan().IndexOf("MARKER"u8);
        at.Should().BePositive("precondition: the stored content is in the file");
        bytes[at] ^= 0x20;
        File.WriteAllBytes(zip, bytes);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = Path.Combine(_temp.Path, "out") });

        result.FullyProcessedSources.Should().NotContain(zip, "the entry failed its CRC check");
    }

    [Fact]
    public async Task Extract_TwoArchivesOneCorrupt_OnlyTheGoodOneDeletable()
    {
        string good = WriteZip("good.zip", ("a.txt", "A"));
        string bad = Path.Combine(_temp.Path, "archives", "bad.zip");
        File.WriteAllBytes(bad, [.. "PK\u0003\u0004"u8, .. new byte[64]]);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [good, bad], DestinationFolder = Path.Combine(_temp.Path, "out") });

        result.FullyProcessedSources.Should().Equal(good);
    }

    [Fact]
    public async Task Archive_EntryFilterLeavesFileOut_SourceNotDeletable()
    {
        string src = SourceFolder("src");
        File.WriteAllText(Path.Combine(src, "skip.log"), "left out by the filter");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = Scratch("dest"),
            EntryFilter = (name, _) => !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase),
        });

        result.FullyProcessedSources.Should().NotContain(src, "skip.log is not in the archive");
    }

    [Fact]
    public async Task Archive_EmptyFileAndEmptyFolderOnly_DeletableOnlyIfBothInArchive()
    {
        string src = SourceFolder("src");
        Directory.CreateDirectory(Path.Combine(src, "empty-folder"));
        File.WriteAllBytes(Path.Combine(src, "empty.bin"), []);
        string dest = Scratch("dest");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions { SourcePaths = [src], DestinationFolder = dest, ArchiveName = "e" });

        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(dest, "e.zip"));
        List<string> names = [.. archive.Entries.Select(e => e.FullName)];
        if (result.FullyProcessedSources.Contains(src))
            names.Should().Contain(n => n.EndsWith("empty-folder/", StringComparison.Ordinal)).And.Contain(n => n.EndsWith("empty.bin", StringComparison.Ordinal));
    }
}

/// <summary>GetShortPathNameW: the 8.3 spelling of an existing path, or the path itself when the volume makes none.</summary>
internal static partial class ShortNames
{
    public static string Of(string path)
    {
        char[] buffer = new char[1024];
        uint length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        if (length == 0 || length >= buffer.Length)
            throw new IOException($"GetShortPathNameW failed for {path}: {Marshal.GetLastPInvokeError()}");
        return new string(buffer, 0, (int)length);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}

/// <summary>A test that needs 8.3 short names on the temp volume; skipped where none are made.</summary>
public sealed class RequiresShortNamesFactAttribute : FactAttribute
{
    public RequiresShortNamesFactAttribute()
    {
        string probe = Path.Combine(Path.GetTempPath(), "pakko-redteam-shortname-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe);
        try
        {
            if (ShortNames.Of(probe) == probe)
                Skip = "the temp volume makes no 8.3 short names";
        }
        catch (IOException ex)
        {
            Skip = "GetShortPathNameW failed: " + ex.Message;
        }
        finally
        {
            Directory.Delete(probe);
        }
    }
}

/// <summary>SUBST of a free drive letter onto a scratch folder; the letter is removed again in Unmap.</summary>
// T-F420: two archives into one folder (x *.zip, Explorer's "Extract here"): the second's a.txt
// replaced the first's, so the first is not wholly on disk any more.
public sealed class CrossArchiveReplacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pakko-tf420-" + Guid.NewGuid().ToString("N"));

    public CrossArchiveReplacementTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort: a scratch folder in %TEMP% */ }
    }

    private string Zip(string name, string content)
    {
        string path = Path.Combine(_root, name);
        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("a.txt").Open()))
            writer.Write(content);
        return path;
    }

    [Theory]
    [InlineData(ConflictBehavior.Overwrite, false)]
    [InlineData(ConflictBehavior.Rename, true)]
    public async Task Extract_TwoArchivesIntoOneFolder_TheEarlierIsDeletableOnlyIfItsFileStayed(ConflictBehavior onConflict, bool firstDeletable)
    {
        string first = Zip("a1.zip", "ONE"), second = Zip("a2.zip", "TWO");

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [first, second], DestinationFolder = Path.Combine(_root, "out"),
            Mode = ExtractMode.SingleFolder, OnConflict = onConflict,
        });

        result.FullyProcessedSources.Should().Equal(firstDeletable ? [first, second] : [second]);
        result.Sources.Single(s => s.Path == first).Cause.Should().Be(firstDeletable ? null : PartialCause.EntryReplaced);
    }
}

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
