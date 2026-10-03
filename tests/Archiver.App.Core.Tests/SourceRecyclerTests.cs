using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F207/T-F242: "Delete after operation" recycles sources on a fixed local volume, asks before
// deleting anything else permanently, and reports every source still on disk afterwards.
public sealed class SourceRecyclerTests
{
    private sealed class FakeOps : ISourceDeleteOperations
    {
        public readonly Dictionary<string, string?> Final = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Fixed = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> OnDisk = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> RecycleLeaves = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> DeleteThrows = new(StringComparer.OrdinalIgnoreCase);
        public bool RecycleThrows;
        public readonly List<IReadOnlyList<string>> RecycleCalls = [];
        public readonly List<string> Deleted = [];

        public void Add(string path, string final, bool isFixed)
        {
            Final[path] = final;
            OnDisk.Add(final);
            if (isFixed) Fixed.Add(final);
        }

        public string? ResolveFinalPath(string path) => Final.GetValueOrDefault(path);
        public bool IsOnFixedLocalVolume(string finalPath) => Fixed.Contains(finalPath);

        public void MoveToRecycleBin(IReadOnlyList<string> finalPaths)
        {
            RecycleCalls.Add(finalPaths);
            if (RecycleThrows) throw new InvalidOperationException("boom");
            foreach (string? p in finalPaths.Where(p => !RecycleLeaves.Contains(p))) OnDisk.Remove(p);
        }

        public void DeletePermanently(string finalPath)
        {
            if (DeleteThrows.Contains(finalPath)) throw new IOException("locked");
            Deleted.Add(finalPath);
            OnDisk.Remove(finalPath);
        }

        public bool Exists(string path) => OnDisk.Contains(path);
    }

    private readonly FakeOps _ops = new();
    private readonly List<IReadOnlyList<string>> _confirmCalls = [];

    private Func<IReadOnlyList<string>, Task<bool>> Confirm(bool answer) => paths =>
    {
        _confirmCalls.Add(paths);
        return Task.FromResult(answer);
    };

    [Fact]
    public async Task FixedLocalSources_RecycledByFinalPath_NoConfirmation_NothingReported()
    {
        _ops.Add(@"Q:\a.zip", @"C:\real\a.zip", isFixed: true);
        _ops.Add(@"C:\b", @"C:\b", isFixed: true);

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"Q:\a.zip", @"C:\b"], Confirm(true))).NotDeleted;

        notDeleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().ContainSingle().Which.Should().Equal(@"C:\real\a.zip", @"C:\b");
        _confirmCalls.Should().BeEmpty();
        _ops.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task RecycledSourceStillOnDisk_ReportedByOriginalPath()
    {
        _ops.Add(@"Q:\a.zip", @"C:\real\a.zip", isFixed: true);
        _ops.RecycleLeaves.Add(@"C:\real\a.zip");

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"Q:\a.zip"], Confirm(true))).NotDeleted;

        notDeleted.Should().Equal(@"Q:\a.zip");
    }

    [Fact]
    public async Task RecycleThrows_EveryCandidateStillOnDiskReported()
    {
        _ops.Add(@"C:\a", @"C:\a", isFixed: true);
        _ops.RecycleThrows = true;

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"C:\a"], Confirm(true))).NotDeleted;

        notDeleted.Should().Equal(@"C:\a");
    }

    [Fact]
    public async Task NonFixedSource_Declined_KeptNotReported_NothingDeleted()
    {
        // Declining is the user's own choice — reporting it right back as "not deleted" was noise.
        _ops.Add(@"Z:\a.zip", @"\\server\share\a.zip", isFixed: false);

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"Z:\a.zip"], Confirm(false))).NotDeleted;

        _confirmCalls.Should().ContainSingle().Which.Should().Equal(@"Z:\a.zip");
        notDeleted.Should().BeEmpty();
        _ops.OnDisk.Should().Contain(@"\\server\share\a.zip");
        _ops.Deleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task NonFixedSources_Confirmed_DeletedPermanently_FailureReported()
    {
        _ops.Add(@"Z:\a.zip", @"\\server\share\a.zip", isFixed: false);
        _ops.Add(@"E:\b.zip", @"E:\b.zip", isFixed: false);
        _ops.DeleteThrows.Add(@"E:\b.zip");

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"Z:\a.zip", @"E:\b.zip"], Confirm(true))).NotDeleted;

        _ops.Deleted.Should().Equal(@"\\server\share\a.zip");
        notDeleted.Should().Equal(@"E:\b.zip");
    }

    [Fact]
    public async Task Mixed_LocalRecycled_RemoteDeclined_NothingReported()
    {
        _ops.Add(@"C:\a.zip", @"C:\a.zip", isFixed: true);
        _ops.Add(@"Z:\b.zip", @"\\server\share\b.zip", isFixed: false);

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"C:\a.zip", @"Z:\b.zip"], Confirm(false))).NotDeleted;

        _ops.RecycleCalls.Should().ContainSingle().Which.Should().Equal(@"C:\a.zip");
        notDeleted.Should().BeEmpty();
        _ops.OnDisk.Should().Contain(@"\\server\share\b.zip");
    }

    // T-F302: the App drops exactly these rows — recycled or deleted, by the path given; a declined
    // or failed source keeps its row.
    [Fact]
    public async Task Deleted_ListsRecycledAndPermanentlyDeleted_NotDeclinedOrFailed()
    {
        _ops.Add(@"Q:\a.zip", @"C:\real\a.zip", isFixed: true);
        _ops.Add(@"C:\stuck", @"C:\stuck", isFixed: true);
        _ops.RecycleLeaves.Add(@"C:\stuck");
        _ops.Add(@"Z:\b.zip", @"\\server\share\b.zip", isFixed: false);
        _ops.Add(@"E:\c.zip", @"E:\c.zip", isFixed: false);
        _ops.DeleteThrows.Add(@"E:\c.zip");

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync(
            [@"Q:\a.zip", @"C:\stuck", @"Z:\b.zip", @"E:\c.zip", @"C:\gone"], Confirm(true));

        result.Deleted.Should().BeEquivalentTo(@"Q:\a.zip", @"Z:\b.zip");
        result.NotDeleted.Should().BeEquivalentTo(@"C:\stuck", @"E:\c.zip", @"C:\gone");
    }

    [Fact]
    public async Task Deleted_DeclinedPermanentDelete_IsNotListed()
    {
        _ops.Add(@"Z:\b.zip", @"\\server\share\b.zip", isFixed: false);

        RecycleResult result = await new SourceRecycler(_ops).DeleteAsync([@"Z:\b.zip"], Confirm(false));

        result.Deleted.Should().BeEmpty();
        result.NotDeleted.Should().BeEmpty();
    }

    [Fact]
    public async Task UnresolvablePath_NeverDeleted_Reported()
    {
        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"C:\gone.zip"], Confirm(true))).NotDeleted;

        notDeleted.Should().Equal(@"C:\gone.zip");
        _ops.RecycleCalls.Should().BeEmpty();
        _confirmCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyInput_NoCallsAtAll()
    {
        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([], Confirm(true))).NotDeleted;

        notDeleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().BeEmpty();
        _confirmCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateSource_ProcessedOnce()
    {
        _ops.Add(@"C:\a.zip", @"C:\a.zip", isFixed: true);

        IReadOnlyList<string> notDeleted = (await new SourceRecycler(_ops).DeleteAsync([@"C:\a.zip", @"c:\A.zip"], Confirm(true))).NotDeleted;

        notDeleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().ContainSingle().Which.Should().ContainSingle();
    }

    // --- Real Win32 operations: resolution, plus one recycle of a test-created temp file ---

    [Fact]
    public void Win32_LocalTempFile_ResolvesToFixedLocalVolume()
    {
        string file = Path.GetTempFileName();
        try
        {
            var ops = new Win32SourceDeleteOperations(() => IntPtr.Zero);
            string? final = ops.ResolveFinalPath(file);

            final.Should().NotBeNull();
            final.Should().NotStartWith(@"\\");
            ops.IsOnFixedLocalVolume(final!).Should().BeTrue();
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Win32_UncPath_NeverTreatedAsRecyclable()
    {
        string file = Path.GetTempFileName();
        try
        {
            string unc = @"\\localhost\" + file[0] + "$" + file[2..];
            var ops = new Win32SourceDeleteOperations(() => IntPtr.Zero);
            string? final = ops.ResolveFinalPath(unc);

            // An admin share may be unreachable on some machines — null is also safe (not deleted).
            if (final is not null)
            {
                final.Should().StartWith(@"\\");
                ops.IsOnFixedLocalVolume(final).Should().BeFalse();
            }
        }
        finally { File.Delete(file); }
    }

    // T-F287: the one real recycle — proves SHFileOperationW's struct and flags still reach the
    // Recycle Bin (a wrong layout or a lost FOF_ALLOWUNDO would delete the file for good).
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Win32_MoveToRecycleBin_FileLandsInRecycleBinNotDeleted()
    {
        string unique = "pakko-recycle-" + Guid.NewGuid().ToString("N") + ".txt";
        string file = Path.Combine(Path.GetTempPath(), unique);
        File.WriteAllText(file, "recycle me");
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        string bin = Path.Combine(Path.GetPathRoot(file)!, "$Recycle.Bin", sid);
        var ops = new Win32SourceDeleteOperations(() => IntPtr.Zero);

        ops.MoveToRecycleBin([ops.ResolveFinalPath(file)!]);

        File.Exists(file).Should().BeFalse();
        string[] info = Directory.EnumerateFiles(bin, "$I*")
            .Where(i => System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(i)).Contains(unique, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (string i in info)
        {
            File.Delete(Path.Combine(bin, "$R" + Path.GetFileName(i)[2..]));
            File.Delete(i);
        }
        info.Should().ContainSingle("the file must be in the Recycle Bin, not deleted permanently");
    }

    [Fact]
    public void Win32_MissingPath_ResolvesToNull()
    {
        new Win32SourceDeleteOperations(() => IntPtr.Zero)
            .ResolveFinalPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .Should().BeNull();
    }
}
