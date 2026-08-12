using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F161: a real user report — extracting an archive with files plus a folder, choosing
// Rename+"apply to all" on a conflict, produced "Cannot extract archive: Access to the path
// '...\<name>_tmp' is denied." and the archive's own folder never appeared at the destination.
//
// Root-caused via an isolated platform probe (see the first test below and DECISIONS.md's T-F161
// entry) rather than guesswork: Directory.Move fails the WHOLE source tree with IOException
// whenever ANY file anywhere inside it is transiently held open by another process (real-time
// antivirus scan, cloud-sync filter, Search Indexer) — even though every file Pakko itself wrote
// has already been closed by this point — and the exception names only the top-level source
// directory, never the specific locked file. That is the exact shape of the user's report (the
// IOException catch branch's "Cannot extract archive:" prefix, and a bare "..._tmp" path with no
// nested subpath). A second, independent bug was found reasoning through the same code path: the
// entry-extraction loop's try/catch only cleans up tempDest on OperationCanceledException, so any
// other failure (a path-traversal rejection, a locked source entry, disk-full, etc.) leaks the
// "_tmp" staging folder on disk forever — a very plausible way the user's Desktop ended up with a
// pre-existing "_tmp" folder in the first place.
public sealed class ZipArchiveServiceExtractTempDestResilienceTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // Documents the real platform behavior the fast-path/merge fallback (implemented below in
    // ZipArchiveService) exists to work around — not a regression test, a fact lock-in. Confirmed
    // empirically 2026-08-11 via this exact probe before any production code was touched.
    [Fact]
    public void DirectoryMove_FileLockedDeepInsideSourceTree_ThrowsIOExceptionNamingOnlyTopLevelPath()
    {
        string src = Path.Combine(_temp.Path, "src", "Sub");
        Directory.CreateDirectory(src);
        string lockedFile = Path.Combine(src, "a.txt");
        File.WriteAllText(lockedFile, "x");
        string dest = Path.Combine(_temp.Path, "dst");
        string sourceRoot = Path.Combine(_temp.Path, "src");

        using var handle = File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        var act = () => Directory.Move(sourceRoot, dest);

        var ex = act.Should().Throw<IOException>().Which;
        ex.Should().NotBeOfType<UnauthorizedAccessException>();
        ex.Message.Should().Be($"Access to the path '{sourceRoot}' is denied.");
    }

    private static string CreateZipWithTraversalEntry(string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using (var s = archive.CreateEntry("good.txt").Open())
        using (var w = new StreamWriter(s))
            w.Write("legit content");
        // Escapes the temp staging directory once combined+resolved — rejected at the
        // path-traversal check inside TryExtractSingleEntryAsync (ZipArchiveService.cs:1111),
        // which throws InvalidDataException and (before this fix) left tempDest on disk.
        using (var s = archive.CreateEntry("../escape.txt").Open())
        using (var w = new StreamWriter(s))
            w.Write("malicious");
        return zipPath;
    }

    [Fact]
    public async Task ExtractAsync_EntryPathTraversal_DoesNotLeaveOrphanedTempFolder()
    {
        string zip = CreateZipWithTraversalEntry(Path.Combine(_temp.Path, "traversal.zip"));

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
            SeparateFolderName = "out",
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeFalse();
        // ExtractOneZipWithErrorMappingAsync's InvalidDataException handler collapses every
        // cause (genuine ZIP corruption or this path-traversal rejection alike) into one generic
        // message — the actual point of this test is the leaked tempDest below, not this text.
        result.Errors.Should().ContainSingle(e => e.Message == "File has ZIP signature but appears corrupted or incomplete.");

        string tempDest = Path.Combine(_temp.Path, "out_tmp");
        Directory.Exists(tempDest).Should().BeFalse("a failed extraction must not leave a stray _tmp staging folder behind");
    }

    // Exercises ZipArchiveService.CommitTempDestToActualDest directly (internal, via
    // InternalsVisibleTo) with an injected move delegate that fails exactly like the real
    // Directory.Move does above — the cheapest deterministic way to prove the fast-path-to-merge
    // fallback actually runs, without needing a real external lock or a full ExtractAsync pass.
    [Fact]
    public void CommitTempDestToActualDest_MoveThrowsIOException_FallsBackToPerFileMerge()
    {
        string tempDest = Path.Combine(_temp.Path, "out_tmp");
        Directory.CreateDirectory(Path.Combine(tempDest, "Sub"));
        File.WriteAllText(Path.Combine(tempDest, "loose1.txt"), "loose1");
        File.WriteAllText(Path.Combine(tempDest, "Sub", "nested1.txt"), "nested1");
        string actualDest = Path.Combine(_temp.Path, "out");

        int moveAttempts = 0;
        void FailingMove(string src, string dst)
        {
            moveAttempts++;
            throw new IOException($"Access to the path '{src}' is denied.");
        }

        var lockedRelativePaths = ZipArchiveService.CommitTempDestToActualDest(tempDest, actualDest, FailingMove);

        moveAttempts.Should().Be(1);
        lockedRelativePaths.Should().BeEmpty("the fast path failed, but the per-file merge fallback moved every real file successfully");
        File.Exists(Path.Combine(actualDest, "loose1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(actualDest, "Sub", "nested1.txt")).Should().BeTrue();
        Directory.Exists(tempDest).Should().BeFalse("every file was successfully merged out, so the empty staging folder must be cleaned up");
    }

    // T-F170: a distinct failure mode from the fast-path Directory.Move bug above — this locks a
    // file directly at the FINAL destination path (not source-tree-internal), forcing
    // CommitTempDestToActualDest into its per-file merge loop (actualDest already exists), where
    // File.Move(file, finalFile, overwrite: true) has no try/catch at all. First written to
    // confirm today's real (broken) behavior before any fix, per this project's test-first rule —
    // see DECISIONS.md's T-F170 entry for the confirmed pre-fix outcome.
    [Fact]
    public async Task ExtractAsync_DestinationFileLockedDuringCommitMerge_OtherEntriesStillExtractPerItemErrorRecorded()
    {
        string destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        string lockedDestFile = Path.Combine(destDir, "conflict.txt");
        File.WriteAllText(lockedDestFile, "pre-existing locked content");

        string zipPath = Path.Combine(_temp.Path, "archive.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var s = archive.CreateEntry("conflict.txt").Open())
            using (var w = new StreamWriter(s))
                w.Write("new content that should overwrite");
            using (var s = archive.CreateEntry("safe.txt").Open())
            using (var w = new StreamWriter(s))
                w.Write("unrelated entry");
        }

        using var handle = File.Open(lockedDestFile, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Overwrite,
        });

        // T-F170 fix: a locked destination file is a per-item failure, not a whole-archive one —
        // safe.txt (no conflict) must still land, and the archive is NOT silently reported as a
        // full success (a file the user asked to overwrite did not arrive).
        result.Success.Should().BeFalse();
        File.ReadAllText(Path.Combine(destDir, "safe.txt")).Should().Be("unrelated entry");
        File.ReadAllText(lockedDestFile).Should().Be("pre-existing locked content",
            "the locked file could not be overwritten, so its original content must survive untouched");
    }

    [Fact]
    public void CommitTempDestToActualDest_ActualDestDoesNotExist_UsesFastPathMove()
    {
        string tempDest = Path.Combine(_temp.Path, "out_tmp");
        Directory.CreateDirectory(tempDest);
        File.WriteAllText(Path.Combine(tempDest, "loose1.txt"), "loose1");
        string actualDest = Path.Combine(_temp.Path, "out");

        int moveAttempts = 0;
        void RealMove(string src, string dst)
        {
            moveAttempts++;
            Directory.Move(src, dst);
        }

        ZipArchiveService.CommitTempDestToActualDest(tempDest, actualDest, RealMove);

        moveAttempts.Should().Be(1);
        File.Exists(Path.Combine(actualDest, "loose1.txt")).Should().BeTrue();
        Directory.Exists(tempDest).Should().BeFalse();
    }
}
