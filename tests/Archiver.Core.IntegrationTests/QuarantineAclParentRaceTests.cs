using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F195: every TarSandboxScope — in every Pakko process — re-granted traverse on the one shared
/// %TEMP%\PakkoTarSandbox parent via SetNamedSecurityInfoW, which (per its documentation)
/// re-propagates inheritable ACEs to EVERY existing child. That walk is a read-recompute-write of
/// each live quarantine's in\/out\ DACL, racing the owning scope's own read-modify-write of its
/// Modify grant — a lost update leaves tar.exe without write access to its own out\ folder.
/// Reproduced in one process here (the race is between the two API calls, not between processes);
/// the cross-process test failures and the App+Shell concurrent-extraction case are the same bug.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class QuarantineAclParentRaceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppContainerProfile _profile = new("Pakko.TarSandbox.Test." + Guid.NewGuid());

    public void Dispose()
    {
        _temp.Dispose();
        try { _profile.Delete(); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ConcurrentParentGrant_NeverLosesAChildScopesModifyGrant()
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();
        var sidIdentifier = new SecurityIdentifier(sid.DangerousGetHandle());
        string parent = _temp.Path;
        QuarantineAcl.EnsureSharedParentTraverse(parent, sid);

        // 300 scopes x 4 hammer threads: reproduced the lost grant in 2 of 3 runs (~3 s each)
        // against the pre-fix SetNamedSecurityInfoW path; the fix makes the parent grant a no-op
        // once present, so this is deterministically green afterwards.
        const int ScopeCount = 300;
        const int HammerThreads = 4;
        using var done = new CancellationTokenSource();
        var hammerFailures = new List<Exception>();

        // Stands in for every OTHER scope (in this or another process) creating its quarantine.
        Task hammer = Task.WhenAll(Enumerable.Range(0, HammerThreads).Select(_ => Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                try { QuarantineAcl.EnsureSharedParentTraverse(parent, sid); }
                catch (InvalidOperationException ex) { lock (hammerFailures) hammerFailures.Add(ex); }
            }
        })));

        var outDirs = new List<string>();
        var grantFailures = new List<Exception>();
        for (int i = 0; i < ScopeCount; i++)
        {
            string scopeRoot = Path.Combine(parent, $"scope{i}");
            string outDir = Path.Combine(scopeRoot, "out");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "busy.bin"), new string('x', 4096));
            try { QuarantineAcl.GrantModify(outDir, sid); }
            catch (InvalidOperationException ex) { grantFailures.Add(ex); }
            outDirs.Add(outDir);
        }

        await done.CancelAsync();
        await hammer;

        grantFailures.Should().BeEmpty();
        hammerFailures.Should().BeEmpty();
        outDirs.Where(d => !HasExplicitModifyGrant(d, sidIdentifier))
            .Should().BeEmpty("a concurrent parent grant must never wipe a live scope's own out\\ grant");
    }

    [Fact]
    public void EnsureSharedParentTraverse_GrantsTraverseAndIsIdempotent()
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();
        string parent = _temp.Path;

        QuarantineAcl.EnsureSharedParentTraverse(parent, sid);
        string before = new DirectoryInfo(parent).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        QuarantineAcl.EnsureSharedParentTraverse(parent, sid);
        string after = new DirectoryInfo(parent).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        after.Should().Be(before);
        before.Should().Contain(new SecurityIdentifier(sid.DangerousGetHandle()).Value);
    }

    private static bool HasExplicitModifyGrant(string path, SecurityIdentifier sid)
    {
        var rules = new DirectoryInfo(path).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>().Any(r =>
            r.IdentityReference.Equals(sid)
            && r.AccessControlType == AccessControlType.Allow
            && (r.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify);
    }
}
