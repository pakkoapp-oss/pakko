using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>
/// Archiver.Shell's Explorer commands (T-F268: moved out of Program.cs). Every window goes through
/// <see cref="IOperationUi"/>: one session per operation, its prompts, and one result message.
/// </summary>
internal sealed class ShellCommands(IOperationUi ui, ShellServices services)
{
    // -------------------------------------------------------------------------
    // --extract-here: extract each archive to its own sibling directory.
    // Uses T-14 smart folder logic in ZipArchiveService (SeparateFolders mode): single root folder →
    // strips prefix; multiple roots → creates wrapper folder; single root FILE (T-F154) → bypasses
    // the per-archive subfolder entirely and lands next to the archive, matching how a real archiver
    // (NanaZip/7-Zip) handles a single-file archive.
    // -------------------------------------------------------------------------
    public Task ExtractHereAsync(IReadOnlyList<string> archivePaths) =>
        RunExtractSelectionAsync(archivePaths, (archivePath, prompts) =>
        {
            string destFolder = Path.GetDirectoryName(archivePath) ?? ".";
            // T-F67: a plain OnConflict=Rename only renames individual conflicting files inside
            // an existing destination folder (that's the GUI app's merge behavior). The shell
            // command instead wants a brand-new numbered folder so re-extracting never silently
            // merges into — or does nothing to — a folder from a previous run.
            string folderName = ArchiveNaming.GetUniqueFolderName(destFolder, ArchiveNaming.GetBaseName(archivePath));
            return new ExtractOptions
            {
                ArchivePaths = [archivePath],
                DestinationFolder = destFolder,
                Mode = ExtractMode.SeparateFolders,
                SeparateFolderName = folderName,
                // T-F155: the fresh numbered folder above makes a real per-file conflict unreachable
                // for a multi-file archive, but T-F154's unisolatedDestDir bypass means a single-root-
                // FILE archive still lands directly in destFolder — the exact case that can collide.
                OnConflict = ConflictBehavior.Ask,
                ResolveConflictAsync = prompts.Conflict.ResolveAsync,
                ResolvePasswordAsync = prompts.Password.ResolveAsync,
                ConfirmCompressionBombExtraction = prompts.ConfirmCompressionBombAsync,
            };
        });

    // -------------------------------------------------------------------------
    // --extract-flat (T-F115): extract every archive with full paths into its own containing folder
    // (7-Zip/NanaZip "Extract here"). No <archive_name>\ wrapper is ever added (T-F156), and since
    // T-F205 an archive's own single root folder is kept too, not unwrapped. Contrast with
    // --extract-here (SeparateFolders mode, above), which always wraps per archive by design, and
    // with ExtractFolderAsync below, which unconditionally pre-computes a fresh subfolder
    // regardless of the archive's own root shape.
    // -------------------------------------------------------------------------
    public Task ExtractHereFlatAsync(IReadOnlyList<string> archivePaths) =>
        RunExtractSelectionAsync(archivePaths, (archivePath, prompts) => new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = Path.GetDirectoryName(archivePath) ?? ".",
            Mode = ExtractMode.SingleFolder,
            // T-F155: this is the path that collides on essentially every file when an archive
            // is re-extracted into its own containing folder a second time.
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = prompts.Conflict.ResolveAsync,
            ResolvePasswordAsync = prompts.Password.ResolveAsync,
            ConfirmCompressionBombExtraction = prompts.ConfirmCompressionBombAsync,
        });

    // -------------------------------------------------------------------------
    // --extract-folder: always extract into an explicit <archive_name>\ subfolder. T-F205: a single
    // root folder named like the archive is dropped (NanaZip's default ElimDup), so name.zip holding
    // name/... does not become name\name\...; any other root folder is kept.
    // -------------------------------------------------------------------------
    public Task ExtractFolderAsync(IReadOnlyList<string> archivePaths) =>
        RunExtractSelectionAsync(archivePaths, (archivePath, prompts) =>
        {
            string archiveDir = Path.GetDirectoryName(archivePath) ?? ".";
            string folderName = ArchiveNaming.GetUniqueFolderName(archiveDir, ArchiveNaming.GetBaseName(archivePath));
            return new ExtractOptions
            {
                ArchivePaths = [archivePath],
                DestinationFolder = Path.Combine(archiveDir, folderName),
                Mode = ExtractMode.SingleFolder,
                EliminateDuplicateRootFolder = true,
                // T-F155: the destination is always a fresh numbered folder, so this realistically
                // stays inert (no on-disk conflict possible) except for duplicate in-archive entry
                // names — wired for parity with the other two commands.
                OnConflict = ConflictBehavior.Ask,
                ResolveConflictAsync = prompts.Conflict.ResolveAsync,
                ResolvePasswordAsync = prompts.Password.ResolveAsync,
                ConfirmCompressionBombExtraction = prompts.ConfirmCompressionBombAsync,
            };
        });

    // -------------------------------------------------------------------------
    // --archive: pack all source paths into a single archive placed next to the first item. A
    // single selected item is named after itself; multiple items are named after their common
    // containing folder instead of an arbitrary selected item (matches NanaZip's convention).
    // -------------------------------------------------------------------------
    public async Task ArchiveAsync(IReadOnlyList<string> sourcePaths, ArchiveContainerFormat format)
    {
        if (sourcePaths.Count == 0)
            return;

        // T-F153: a source path ending in a directory separator (e.g. an "--archive" CLI argument
        // typed/tab-completed with a trailing "\") made Path.GetDirectoryName(firstPath) below return
        // firstPath's OWN full path instead of its parent — placing the new archive INSIDE the source
        // folder itself rather than next to it — and separately made Path.GetFileNameWithoutExtension
        // return "", falling the archive's name back to the generic "archive" instead of the real
        // folder name. The routers normalize this internally too, but this destFolder/archiveName
        // computation happens here, before any of that code runs.
        sourcePaths = [.. sourcePaths.Select(Path.TrimEndingDirectorySeparator)];
        string firstPath = sourcePaths[0];
        // T-F99: Path.GetDirectoryName returns null when firstPath is itself a root (e.g. "Z:\") —
        // a root has no parent to place the archive next to. Falls back to Desktop, the same default
        // destination MainViewModel.cs already uses, rather than "." (the process's own working
        // directory, which is unpredictable for a COM-surrogate-launched process).
        string destFolder = Path.GetDirectoryName(firstPath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // T-F264: the same rule the App and the Explorer menu title use.
        string archiveName = ArchiveNaming.GetDefaultArchiveName(sourcePaths);

        IArchiveCreationRouter router = services.CreateArchiveCreationRouter();
        var options = new ArchiveOptions
        {
            SourcePaths = sourcePaths,
            DestinationFolder = destFolder,
            ArchiveName = archiveName,
            Mode = ArchiveMode.SingleArchive,
            OnConflict = ConflictBehavior.Rename,
            Format = format,
        };

        await RunArchiveOperationAsync(OperationTextLocalizer.Get("TitleArchiving", archiveName),
            session => router.ArchiveAsync(options, session.Progress, session.Cancellation)).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // --test: verify every entry's CRC-32 across all selected archives without writing anything.
    // One TestAsync call spans the whole selection, so Core's own PasswordResolver already keeps
    // "apply to remaining" across archives — no StickyCallback here (unlike the extract commands,
    // which call ExtractAsync once per archive). T-F261: through the router, so Group Policy and
    // format detection are Core's, not a ZIP-engine-for-everything shortcut.
    // -------------------------------------------------------------------------
    public async Task TestAsync(IReadOnlyList<string> archivePaths)
    {
        IExtractionRouter service = await services.CreateExtractionRouterAsync().ConfigureAwait(false);
        string title = archivePaths.Count == 1
            ? OperationTextLocalizer.Get("TitleTesting", Path.GetFileName(archivePaths[0]))
            : OperationTextLocalizer.Get("TitleTestingMany", archivePaths.Count);

        using IOperationSession session = ui.Begin(title, ProgressStyle.Bytes, endsWithResult: true);
        ArchiveResult result;
        try
        {
            result = await service.TestAsync(archivePaths, session.Progress,
                info => session.AskPasswordAsync(info, canApplyToRemaining: archivePaths.Count > 1),
                session.Cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (session.Cancellation.IsCancellationRequested)
            return;

        session.Complete(OperationMessages.ForTestResult(title, result));
    }

    // -------------------------------------------------------------------------
    // --hash (T-F128): CRC-32/SHA-256 for the "Hash" context-menu submenu.
    // -------------------------------------------------------------------------
    public async Task HashAsync(IReadOnlyList<string> paths, HashAlgorithmKind algorithm)
    {
        string label = algorithm == HashAlgorithmKind.Crc32 ? "CRC-32" : "SHA-256";
        // T-F153: Path.TrimEndingDirectorySeparator (not just a bare TrimEnd('\\')) also handles a
        // forward-slash trailing separator — display-only here (the title bar), but kept consistent
        // with ArchiveAsync's functional fix.
        string title = paths.Count == 1
            ? $"{label}: {Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0]))}"
            : OperationTextLocalizer.Get("TitleHashMany", label, paths.Count);

        using IOperationSession session = ui.Begin(title, ProgressStyle.Bytes, endsWithResult: true);
        HashResult result;
        try
        {
            result = await FileHashService.ComputeAsync(paths, algorithm, session.Progress, session.Cancellation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (session.Cancellation.IsCancellationRequested)
            return;

        session.Complete(OperationMessages.ForHash(title, result, paths));
    }

    // -------------------------------------------------------------------------
    // --scan (T-F146): AMSI-based threat scan of an archive's contents. A single ScanAsync call
    // spans the whole selection, so Core's own PasswordResolver keeps "apply to remaining" —
    // no StickyCallback needed.
    // -------------------------------------------------------------------------
    public async Task ScanAsync(IReadOnlyList<string> archivePaths)
    {
        string title = archivePaths.Count == 1
            ? OperationTextLocalizer.Get("TitleScanning", Path.GetFileName(archivePaths[0]))
            : OperationTextLocalizer.Get("TitleScanningMany", archivePaths.Count);

        IAntivirusScanService service = await services.CreateScanServiceAsync().ConfigureAwait(false);
        using IOperationSession session = ui.Begin(title, ProgressStyle.Percent, endsWithResult: true);
        var options = new AntivirusScanOptions
        {
            ArchivePaths = archivePaths,
            ResolvePasswordAsync = info => session.AskPasswordAsync(info, canApplyToRemaining: archivePaths.Count > 1),
        };

        ThreatScanResult result;
        try
        {
            result = await service.ScanAsync(options, session.Progress, session.Cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (session.Cancellation.IsCancellationRequested)
            return;

        session.Complete(OperationMessages.ForScan(title, result, archivePaths.Count));
    }

    // Open-UI flow (T-F232): hand the selection to Archiver.App as Launch arguments and exit — the
    // App takes over the user interaction; only a failed hand-off is shown here.
    public void OpenUi(LaunchOperation operation, IReadOnlyList<string> files)
    {
        if (OperationMessages.ForLaunch(services.LaunchApp(operation, files)) is { } message)
            ui.ShowMessage(message);
    }

    // One session per archive; cancelling ends the operation with no message, as before.
    // T-F268 step 3: one window for the whole selection, and one combined result at the end.
    // T-F269: a cancel ends the whole command — the session's one token is shared by every archive,
    // and the router throws OperationCanceledException for a cancelled token (T-F260).
    // A cancel Core no longer checks (a prompt on the last entry answered Skip, T-F268 step 5) still
    // shows nothing: each command checks the token again before its result.
    private async Task RunExtractSelectionAsync(
        IReadOnlyList<string> archivePaths, Func<string, SelectionPrompts, ExtractOptions> buildOptions)
    {
        if (archivePaths.Count == 0)
            return;

        IExtractionRouter router = await services.CreateExtractionRouterAsync().ConfigureAwait(false);
        var prompts = new SelectionPrompts(archivePaths.Count);
        string title = archivePaths.Count == 1
            ? OperationTextLocalizer.Get("TitleExtracting", Path.GetFileName(archivePaths[0]))
            : OperationTextLocalizer.Get("TitleExtractingMany", archivePaths.Count);

        using IOperationSession session = ui.Begin(title, ProgressStyle.Bytes);
        prompts.Current = session;

        var results = new List<ArchiveResult>(archivePaths.Count);
        try
        {
            for (int i = 0; i < archivePaths.Count; i++)
            {
                string archivePath = archivePaths[i];
                session.BeginItem(Path.GetFileName(archivePath), i + 1, archivePaths.Count);
                ExtractOptions options = buildOptions(archivePath, prompts);
                results.Add(await router.ExtractAsync(options, session.Progress, session.Cancellation).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (session.Cancellation.IsCancellationRequested)
            return;

        session.Complete(OperationMessages.ForArchiveResult(title, Combine(results)));
    }

    internal static ArchiveResult Combine(List<ArchiveResult> results) => results.Count == 1
        ? results[0]
        : new ArchiveResult
        {
            CreatedFiles = [.. results.SelectMany(r => r.CreatedFiles)],
            Errors = [.. results.SelectMany(r => r.Errors)],
            SkippedFiles = [.. results.SelectMany(r => r.SkippedFiles)],
            Warnings = [.. results.SelectMany(r => r.Warnings)],
            Sources = [.. results.SelectMany(r => r.Sources)],
        };

    private async Task RunArchiveOperationAsync(string title, Func<IOperationSession, Task<ArchiveResult>> op)
    {
        using IOperationSession session = ui.Begin(title, ProgressStyle.Bytes);

        ArchiveResult result;
        try
        {
            result = await op(session).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (session.Cancellation.IsCancellationRequested)
            return;

        session.Complete(OperationMessages.ForArchiveResult(title, result));
    }

    // T-F155/T-F192: one pair of sticky wrappers per Explorer invocation (not per archive), so an
    // "apply to all/remaining" answer spans the whole multi-select — see StickyCallback's doc
    // comment. Each prompt is shown by the session of the archive being extracted at the time.
    private sealed class SelectionPrompts
    {
        public SelectionPrompts(int archiveCount)
        {
            Conflict = new StickyCallback<ConflictInfo, ConflictDecision>(
                info => CurrentSession.AskConflictAsync(info), d => d.ApplyToAll);
            Password = new StickyCallback<PasswordPromptInfo, PasswordDecision>(
                info => CurrentSession.AskPasswordAsync(info, canApplyToRemaining: archiveCount > 1), d => d.ApplyToRemaining,
                whenReused: d => d with { Remembered = true }); // T-F301: Core then says it did not fit
        }

        public IOperationSession? Current { get; set; }

        public StickyCallback<ConflictInfo, ConflictDecision> Conflict { get; }

        public StickyCallback<PasswordPromptInfo, PasswordDecision> Password { get; }

        // T-F217: asked per archive, never "for all" — each suspected bomb gets its own answer.
        public Task<bool> ConfirmCompressionBombAsync(CompressionBombWarning warning) =>
            CurrentSession.ConfirmAsync(OperationMessages.ForCompressionBomb(warning));

        private IOperationSession CurrentSession =>
            Current ?? throw new InvalidOperationException("A prompt was raised outside an operation session.");
    }
}
