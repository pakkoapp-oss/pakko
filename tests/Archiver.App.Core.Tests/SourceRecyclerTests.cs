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
            foreach (var p in finalPaths.Where(p => !RecycleLeaves.Contains(p))) OnDisk.Remove(p);
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

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"Q:\a.zip", @"C:\b"], Confirm(true));

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

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"Q:\a.zip"], Confirm(true));

        notDeleted.Should().Equal(@"Q:\a.zip");
    }

    [Fact]
    public async Task RecycleThrows_EveryCandidateStillOnDiskReported()
    {
        _ops.Add(@"C:\a", @"C:\a", isFixed: true);
        _ops.RecycleThrows = true;

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"C:\a"], Confirm(true));

        notDeleted.Should().Equal(@"C:\a");
    }

    [Fact]
    public async Task NonFixedSource_Declined_KeptAndReported_NothingDeleted()
    {
        _ops.Add(@"Z:\a.zip", @"\\server\share\a.zip", isFixed: false);

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"Z:\a.zip"], Confirm(false));

        _confirmCalls.Should().ContainSingle().Which.Should().Equal(@"Z:\a.zip");
        notDeleted.Should().Equal(@"Z:\a.zip");
        _ops.Deleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task NonFixedSources_Confirmed_DeletedPermanently_FailureReported()
    {
        _ops.Add(@"Z:\a.zip", @"\\server\share\a.zip", isFixed: false);
        _ops.Add(@"E:\b.zip", @"E:\b.zip", isFixed: false);
        _ops.DeleteThrows.Add(@"E:\b.zip");

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"Z:\a.zip", @"E:\b.zip"], Confirm(true));

        _ops.Deleted.Should().Equal(@"\\server\share\a.zip");
        notDeleted.Should().Equal(@"E:\b.zip");
    }

    [Fact]
    public async Task Mixed_LocalRecycled_RemoteDeclined_OnlyRemoteReported()
    {
        _ops.Add(@"C:\a.zip", @"C:\a.zip", isFixed: true);
        _ops.Add(@"Z:\b.zip", @"\\server\share\b.zip", isFixed: false);

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"C:\a.zip", @"Z:\b.zip"], Confirm(false));

        _ops.RecycleCalls.Should().ContainSingle().Which.Should().Equal(@"C:\a.zip");
        notDeleted.Should().Equal(@"Z:\b.zip");
    }

    [Fact]
    public async Task UnresolvablePath_NeverDeleted_Reported()
    {
        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"C:\gone.zip"], Confirm(true));

        notDeleted.Should().Equal(@"C:\gone.zip");
        _ops.RecycleCalls.Should().BeEmpty();
        _confirmCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyInput_NoCallsAtAll()
    {
        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([], Confirm(true));

        notDeleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().BeEmpty();
        _confirmCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateSource_ProcessedOnce()
    {
        _ops.Add(@"C:\a.zip", @"C:\a.zip", isFixed: true);

        var notDeleted = await new SourceRecycler(_ops).DeleteAsync([@"C:\a.zip", @"c:\A.zip"], Confirm(true));

        notDeleted.Should().BeEmpty();
        _ops.RecycleCalls.Should().ContainSingle().Which.Should().ContainSingle();
    }

    // --- Real Win32 operations: resolution only, nothing is deleted ---

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

    [Fact]
    public void Win32_MissingPath_ResolvesToNull()
    {
        new Win32SourceDeleteOperations(() => IntPtr.Zero)
            .ResolveFinalPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .Should().BeNull();
    }
}
