using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core.Tests;

// T-F397: stands for tar.exe testing a tar.gz, so that no test here starts tar.exe: it reads the
// archive to its end, passes it, and says that only the structure was checked.
internal sealed class StructureOnlyTarEngine : ITarService
{
    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ArchiveResult
        {
            Warnings = [.. archivePaths.Select(path => CoreMessages.Warning(path, CoreMessages.Text(MessageCode.TestedStructureOnly)))],
            Sources = [.. archivePaths.Select(path => new SourceResult { Path = path, Outcome = SourceOutcome.Completed })],
        });

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nothing is extracted here.");

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nothing is listed here.");

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nothing is created here.");
}
