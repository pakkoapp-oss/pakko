# DIAGRAMS.md — Required Diagrams for Dead-End & Problem-Area Detection

Diagrams here are a reasoning aid, not an executable test — they don't replace `dotnet test`,
they catch what unit tests structurally can't: illegal/missing state transitions, COM contract
mismatches across process boundaries, and unhandled branches in multi-step validation chains.

## Ground Truth Rule — read before drawing or updating any diagram

**A diagram must reproduce what the code actually does, verified by reading it — never what
seems plausible, symmetric, or "probably how it works."** Specifically:

- Every arrow, branch, and label must trace to a specific file:line you have actually read in
  the current session. If you have not opened the file, you do not know the branch — go read it.
- Do not smooth an asymmetric or ugly code structure into a tidy diagram. If the code has two
  `if`s with an implicit fallthrough (no `else`), draw exactly that — not three clean parallel
  branches. The ugliness is often the point (see the `OnConflict` gate in diagram 3: `Overwrite`
  has no explicit branch and that's a real, load-bearing fact about the code, not an omission
  to tidy up).
- Do not infer execution order from what would be "natural" — verify it. Two operations that
  look independent (e.g. "set `IsBusy=false`" vs. "await a modal dialog") may execute in either
  order depending on where in the method they actually appear; get this from the source, not from
  intuition (see diagram 2, where this was gotten backwards in an earlier draft — the dialog
  await happens *before* `finally`, not after).
- Where the code doesn't name something the diagram needs a label for (e.g. bucketing several
  `catch` blocks into "outcomes"), the label must be immediately followed by the literal
  condition from the code it stands for. Never let an invented label stand alone as if it were
  a real enum or state in the codebase.
- If, while drawing, you find the diagram doesn't match the code you just read, that's a signal
  either the diagram was wrong or the code has a real gap — report both, don't quietly pick
  whichever is more convenient to draw.
- When updating a diagram after a code change, re-derive the affected part from the new source —
  don't edit the old diagram by pattern-matching against its previous shape.

Violating this makes the diagram actively worse than having none: a wrong diagram is trusted
documentation that lies.

---

## When to update which diagram (Definition of Done)

| Change touches... | Update this diagram | Because |
|---|---|---|
| COM interop, `IExplorerCommand`, process launch (`CreateProcess`, `Process.Start`), `IProgressDialog` | **1. Sequence** | Every real bug so far here (`S_FALSE`, missing `[PreserveSig]`, undeclared `Application`) was a contract mismatch across a process/COM boundary — invisible in unit tests, visible in a sequence diagram. |
| `IsBusy`/cancellation/operation lifecycle in `MainViewModel`, or `NativeProgressDialog` cancel polling | **2. State** | Catches stuck states (a state with no outgoing transition) and commands gated on the wrong `CanExecute`. |
| New branch in `ZipArchiveService` validation/conflict/smart-folder logic | **3. Activity** | Catches silently-dropped entries: a new `continue`/skip path that isn't reflected in `ArchiveResult` (see Finding 2 below for why this matters). |
| MSIX manifest `<Application>` entries, `com:ComServer` registration, packaging of a new satellite EXE | **4. Component** | Catches "works in VS, `ERROR_ACCESS_DENIED` when packaged" — an EXE that isn't its own declared `Application` entry. |
| New branch in `TarProcessService`'s pre-scan/extraction/conflict pipeline | **5. Activity (tar.exe)** | Whole-archive-reject means a single scan gap silently lets an entire class of unsafe entries through — there's no per-entry fallback the way ZIP has, so a missed branch here is higher-severity, not lower. |

Update the diagram in the same commit as the code change, alongside `dotnet test` — not as a
follow-up. Re-derive the affected part from the current source per the Ground Truth Rule above;
do not edit by pattern-matching the diagram's previous shape.

---

## 1. Sequence — Shell context-menu invocation

Sources read for this diagram: `src/Archiver.ShellExtension/dllmain.cpp`,
`src/Archiver.ShellExtension/ExplorerCommands.cpp`, `src/Archiver.ShellExtension/ShellExtUtils.cpp`,
`src/Archiver.Shell/Program.cs`, `src/Archiver.Shell/ShellResultPresenter.cs`,
`src/Archiver.Shell/NativeProgressDialog.cs`, `src/Archiver.App/App.xaml.cs`.

```mermaid
sequenceDiagram
    actor User
    participant Explorer as explorer.exe
    participant Dllhost as dllhost.exe (COM surrogate)
    participant Factory as PakkoClassFactory<PakkoRootCommand>
    participant Root as PakkoRootCommand
    participant Enum as SubCommandEnum
    participant EDC as ExtractDialogCommand
    participant EH as ExtractHereCommand
    participant EF as ExtractFolderCommand
    participant CDC as CompressDialogCommand
    participant AC as ArchiveCommand
    participant TC as TestCommand
    participant ShellExe as Archiver.Shell.exe
    participant Core as ZipArchiveService
    participant Dlg as IProgressDialog (shell32)
    participant App as Archiver.App.exe (pakko:// activation)

    User->>Explorer: right-click selection
    Explorer->>Dllhost: CoCreateInstance(CLSID_PakkoRootCommand)<br/>(com:SurrogateServer registration)
    Dllhost->>Factory: DllGetClassObject(CLSID_PakkoRootCommand)<br/>only this CLSID is registered
    Explorer->>Factory: IClassFactory::CreateInstance
    Factory->>Root: Make<PakkoRootCommand>()
    Explorer->>Root: GetFlags() → ECF_HASSUBCOMMANDS
    Explorer->>Root: EnumSubCommands()
    Root->>EDC: Make<ExtractDialogCommand>()
    Root->>EH: Make<ExtractHereCommand>()
    Root->>EF: Make<ExtractFolderCommand>()
    Root->>CDC: Make<CompressDialogCommand>()
    Root->>AC: Make<ArchiveCommand>()
    Root->>TC: Make<TestCommand>()
    Root->>Enum: SetCommands([EDC, EH, EF, CDC, AC, TC])<br/>ALWAYS all six, unconditionally —<br/>selection does not filter EnumSubCommands.<br/>Order mirrors NanaZip's real ContextMenu.cpp (T-F63):<br/>dialog command before its one-click sibling in each group.<br/>TC last: diagnostic/verification action, not primary —<br/>deliberate deviation from NanaZip's own Test-before-Compress grouping
    Root-->>Explorer: Enum (IEnumExplorerCommand)
    loop Explorer drains the enumerator
        Explorer->>Enum: Next(celt, ...)
        Enum-->>Explorer: fetched item(s);<br/>S_OK if fetched==celt, else S_FALSE<br/>(S_FALSE is a SUCCESS code here, not failure)
    end
    Note over Explorer,TC: Visibility is decided per-command by GetState(),<br/>separately from enumeration
    Explorer->>EDC: GetState(psia) → ECS_ENABLED iff AnyPathIsSupportedArchive(paths), else ECS_HIDDEN<br/>(T-F86: also true for RAR/7z/tar-family when tar.exe exists — EDC routes<br/>to Archiver.App/IExtractionRouter, which supports those formats since T-F85)
    Explorer->>EH: GetState(psia) → ECS_ENABLED iff AllPathsAreSupportedArchive(paths), else ECS_HIDDEN<br/>(T-F86: also true for RAR/7z/tar-family when tar.exe exists)
    Explorer->>EF: GetState(psia) → ECS_ENABLED iff AllPathsAreSupportedArchive(paths), else ECS_HIDDEN<br/>(T-F86: also true for RAR/7z/tar-family when tar.exe exists)
    Explorer->>CDC: GetState(psia) → always ECS_ENABLED (T-F63: shown for any selection,<br/>unlike AC below — archiving a .zip into a new .zip via the dialog is valid)
    Explorer->>AC: GetState(psia) → ECS_HIDDEN iff AllPathsAreZip(paths), else ECS_ENABLED<br/>(condition is INVERTED vs. EH/EF; T-F86: deliberately UNCHANGED —<br/>still AllPathsAreZip, not AllPathsAreSupportedArchive — archiving an<br/>all-RAR selection into a new ZIP stays valid, same reasoning as CDC)
    Explorer->>AC: GetTitle(psia) → BuildAddToArchiveTitle(paths)<br/>dynamic "Add to <name>.zip", truncated middle if >40 chars
    Explorer->>TC: GetState(psia) → ECS_ENABLED iff AnyPathIsZip(paths), else ECS_HIDDEN<br/>(T-F62: AnyPathIsZip, NOT AllPathsAreZip — shows on a mixed selection too;<br/>T-F86: deliberately UNCHANGED — ITarService has no Test/verify method,<br/>so enabling this for RAR/7z would run ZipArchiveService.TestAsync,<br/>which skips non-zip paths internally and would report a false<br/>"No errors detected" — see DECISIONS.md's T-F86 entry)
    User->>Explorer: click one visible leaf command
    alt command is EDC or CDC (dialog form, T-F63)
        Explorer->>EDC: Invoke(psia, pbc) — or CDC, same shape
        EDC->>ShellExe: LaunchShellExe(BuildOpenUiExtractArgs(paths))<br/>— or BuildOpenUiArchiveArgs for CDC —<br/>i.e. "--open-ui --extract/--archive <paths>"
        EDC-->>Explorer: S_OK, or HRESULT_FROM_WIN32(GetLastError())
        ShellExe->>App: Process.Start("pakko://extract?files=<base64>", UseShellExecute:true)<br/>— or pakko://archive — then ShellExe's Main returns/exits immediately;<br/>NO NativeProgressDialog, NO ZipArchiveService call in this branch at all
        Note over App: T-F83 (fixed 2026-07-06): cold start reads the activation via<br/>OnLaunched→AppInstance.GetCurrent().GetActivatedEventArgs(), not just<br/>the OnActivated event (which only fires for redirected/warm activation).<br/>Before the fix, a cold pakko:// launch silently opened an EMPTY window.
        App->>App: MainViewModel.AddPathsFromProtocolUri(uri)<br/>— files pre-loaded, user drives Archive/Extract from the full UI
    else command is EH, EF, AC, or TC (silent form)
        Explorer->>EH: Invoke(psia, pbc) — or EF / AC / TC, same shape
        alt GetPathsFromShellItemArray(psia) empty
            EH-->>Explorer: E_INVALIDARG
        else paths present
            EH->>ShellExe: LaunchShellExe(BuildExtractHereArgs(paths))<br/>— or BuildExtractFolderArgs / BuildArchiveArgs / BuildTestArgs<br/>CreateProcessW; PROCESS_INFORMATION handles<br/>closed immediately; does NOT wait for the child<br/>note: TC passes the FULL selection unfiltered — Core does the<br/>per-path IsZipFile gating, same as Extract already does
            ShellExe-->>Explorer: (no return channel — ShellExe runs independently)
            EH-->>Explorer: S_OK, or HRESULT_FROM_WIN32(GetLastError())<br/>on CreateProcess failure — returned the instant<br/>CreateProcess returns, NOT when the operation finishes
            ShellExe->>Dlg: new NativeProgressDialog(title)<br/>= new ProgressDialogCoClass() + StartProgressDialog
            alt COMException thrown during construction
                ShellExe->>Core: ArchiveAsync/ExtractAsync/TestAsync(options or paths, progress: null, CancellationToken.None)
            else dialog constructed
                loop every 250ms (System.Threading.Timer, lock-guarded on dialogLock)
                    ShellExe->>Dlg: HasUserCancelled()<br/>[PreserveSig] required — plain BOOL return, not HRESULT
                    alt returns true
                        ShellExe->>ShellExe: cts.Cancel()
                    end
                end
                ShellExe->>Core: ArchiveAsync/ExtractAsync/TestAsync(options or paths, progress, cts.Token)
                Core-->>ShellExe: IProgress<ProgressReport> callback per file/entry<br/>(TestAsync: TotalBytes=0, one report per archive — no byte-level tracking)
                ShellExe->>Dlg: SetLine(1, CurrentFile) / SetLine(2, status) / SetProgress64(bytes, total)
                alt OperationCanceledException from Core
                    ShellExe-->>ShellExe: return new ArchiveResult { Success = false }
                else Core completes
                    Core-->>ShellExe: ArchiveResult<br/>(TestAsync: CreatedFiles always empty — nothing is written to disk)
                end
                ShellExe->>Dlg: Dispose() → StopProgressDialog
            end
            Note over ShellExe: ShellResultPresenter.Classify(result) (T-F68, fixed 2026-07-06):<br/>Failed (!Success or Errors.Count>0) wins over SkippedOnly wins over Success
            opt Classify == Failed
                ShellExe->>User: MessageBoxW(error summary, max 10 lines shown, MB_ICONERROR)
            end
            opt Classify == SkippedOnly
                ShellExe->>User: MessageBoxW("N entries skipped: ...", MB_ICONWARNING)<br/>T-F68: previously this case (Success=true, Errors=0, Skipped>0)<br/>closed with NO dialog at all — silently indistinguishable from a normal run
            end
            opt result.Success AND command == Test
                ShellExe->>User: MessageBoxW("No errors detected in the archive(s).", MB_ICONINFORMATION)<br/>Test-only: unlike Extract/Archive, success has no visible disk<br/>side effect, so silent success would look like nothing happened
            end
        end
    end
```

**What this catches (verified against the real bugs already fixed here):**
- `EH`/`EF`/`AC`/`EDC`/`CDC`/`TC` `Invoke()` never awaits the operation — Explorer's HRESULT comes
  back the instant `CreateProcess` returns. Anything that assumes Explorer "waits" for Pakko's
  result is wrong.
- **`EDC`/`CDC` (T-F63) take a structurally different path than the other four:** no
  `NativeProgressDialog`, no `ZipArchiveService` call from `Archiver.Shell.exe` at all — they only
  construct a `pakko://` URI and hand off to `Archiver.App` via `Process.Start`/`UseShellExecute`.
  A future change to the silent path's progress/result handling does not automatically apply here.
- **T-F83 (fixed 2026-07-06):** this dialog path is exactly what surfaced a pre-existing cold-start
  bug in `Archiver.App` — `AppInstance.Activated` only fires for *redirected* activation to an
  already-running instance, never for the process's own initial activation, so `OnLaunched` must
  pull `GetActivatedEventArgs()` itself. See `DECISIONS.md`'s "T-F83" entry.
- `HasUserCancelled()` is the one `IProgressDialog` method returning a plain `BOOL`; the
  `[PreserveSig]` boundary is exactly where "Cancel does nothing" lived (`NativeProgressDialog.cs:26`).
- `SubCommandEnum::Next()` returns `S_FALSE` on partial fetch — a *success* code, per
  `(fetched == celt) ? S_OK : S_FALSE` (`ExplorerCommands.cpp:29`). Any new
  `IEnumExplorerCommand`/`IExplorerCommand` method must not conflate `S_FALSE` with failure.
- Visibility filtering happens via `GetState()`, not `EnumSubCommands()` — a future change that
  tries to filter which commands appear by editing `EnumSubCommands` (e.g. "don't enumerate
  Archive for all-ZIP selections") would be editing the wrong method; `ArchiveCommand`'s
  `GetState` condition is the *inverse* of `ExtractHereCommand`/`ExtractFolderCommand`'s, which is
  easy to get backwards when copy-pasting.
- `TestCommand::GetState` (T-F62) uses `AnyPathIsZip`, a condition also shared by `ExtractDialogCommand`
  (T-F63) but distinct from `AllPathsAreZip` (EH/EF) and its inverse (AC) — copy-pasting
  `AllPathsAreZip` here would hide Test/ExtractDialog on any mixed selection, unlike NanaZip's
  reference behavior (verified against real
  NanaZip source in `DECISIONS.md`).
- **T-F86:** `EH`/`EF`/`EDC` moved from `AllPathsAreZip`/`AnyPathIsZip` to new
  `AllPathsAreSupportedArchive`/`AnyPathIsSupportedArchive` (extension allowlist + `tar.exe`
  existence check — no magic-byte read at `GetState()` time, deliberately deviating from
  NanaZip's real exclusion-list shape; see `DECISIONS.md`). `TC` and `AC` were deliberately left
  on the old ZIP-only predicates: `TC` because `ITarService` has no Test capability (enabling it
  would produce a false "No errors detected" for an untested RAR/7z), `AC` because hiding "Add to
  archive…" for an all-RAR selection was never correct to begin with. A future change that makes
  these four commands' gates "consistent" by copy-pasting one predicate onto all of them would
  reintroduce either the false-Test-pass bug or hide a legitimate archive action.

---

## 2. State — Operation lifecycle (`MainViewModel`)

Source read for this diagram: `src/Archiver.App/ViewModels/MainViewModel.cs`
(`ArchiveAsync`/`ExtractAsync`/`Cancel`, lines 271–489 as of T-F85 — shifted from the
diagram's original 228–437 by T-F85's added `IExtractionRouter` field/constructor param and
`_extractableTypes` allowlist above `ArchiveAsync`; the state machine itself is unchanged, only
line numbers moved). Both methods have the identical try/catch/finally shape; the diagram
applies to either. T-F85 changed `ExtractAsync()`'s single `_archiveService.ExtractAsync(...)`
call to `_extractionRouter.ExtractAsync(...)` — same await, same exception types, no new
branch — so this diagram's content is otherwise unaffected by that change.

**T-F05 (Archive Browser):** `ExtractAsync()`'s body was extracted into a shared
`RunExtractAsync(archivePaths, selectedEntryPaths)`, now also called by
`ExtractSelectedFromBrowserCommand`/`ExtractAllFromBrowserCommand`/
`ExtractSingleBrowserEntryAsync`. The state machine below is unchanged — same
`Idle→Busy→{AwaitingSummaryDialog|AwaitingErrorDialog|CancelledNoDialog}→Idle` shape, same
`IsBusy` sequencing — only the transition's trigger label gains three more command names that
all lead to the identical `Busy` entry point via the same shared method body.

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Busy: ArchiveCommand/ExtractCommand invoked<br/>(CanExecute: FileItems.Count>0 && !IsBusy)<br/>IsBusy=true
    Busy --> Busy: CancelCommand invoked<br/>(CanExecute: IsOperationRunning == IsBusy)<br/>→ cts.Cancel() only — IsBusy is NOT changed here;<br/>there is no dedicated "Cancelling" state in code
    Busy --> AwaitingSummaryDialog: _archiveService call returns without throwing<br/>StatusMessage set to StatusDone/StatusArchivedIn<br/>(Errors==0 && Skipped==0) or StatusIssues (otherwise)
    AwaitingSummaryDialog --> AwaitingSummaryDialog: await ShowOperationSummaryAsync(...)<br/>IsBusy is STILL TRUE while this modal is open —<br/>finally has not run yet
    AwaitingSummaryDialog --> Idle: finally{no IsBusy change}; wasCancelled==false so the delay<br/>branch below is skipped; THEN IsBusy=false; THEN StatusMessage=StatusReady<br/>(T-F70: IsBusy=false moved out of finally to here)
    Busy --> AwaitingErrorDialog: unexpected Exception caught (not OperationCanceledException)<br/>StatusMessage="Error"
    AwaitingErrorDialog --> AwaitingErrorDialog: await ShowErrorAsync(...)<br/>IsBusy is STILL TRUE while this modal is open
    AwaitingErrorDialog --> Idle: finally{no IsBusy change}; delay branch skipped;<br/>THEN IsBusy=false; THEN StatusMessage=StatusReady (same T-F70 point as above)
    Busy --> CancelledNoDialog: OperationCanceledException caught<br/>StatusMessage=StatusCancelled — NO dialog is shown
    CancelledNoDialog --> Idle: finally{no IsBusy change}; THEN await Task.Delay(2000)<br/>(IsBusy still TRUE throughout the delay — T-F70 fix);<br/>THEN IsBusy=false; THEN StatusMessage=StatusReady
```

**What this catches:**
- Every exit path sets `IsBusy=false` exactly once, after both the dialog-await (success/issues/
  error) and the cancel-only delay have finished — no path leaves `Busy` without eventually
  re-enabling controls, and (post-T-F70) no path re-enables them early either. A future edit that
  adds an early `return` before this point, or a new `catch` that doesn't fall through to it,
  would break this.
- **T-F70 fix (2026-07-06):** `IsBusy = false` used to live in `finally`, which ran *before* the
  cancel-only `Task.Delay(2000)` — so for those 2 seconds the UI was already not-busy (new
  operations invokable) while the status text still read "Cancelled", unlike the other three
  outcomes where `IsBusy` stays `true` for exactly as long as their dialog is open. Fixed by moving
  `IsBusy = false` to immediately before the final `StatusMessage = StatusReady` line, after the
  `if (wasCancelled) await Task.Delay(2000)` — see `DECISIONS.md`'s "T-F70" entry. All four exit
  paths now release `IsBusy` at the same conceptual point: once nothing transient is left on screen.
- `Cancel`'s `CanExecute` is gated on `IsOperationRunning` (=`IsBusy`) — a future state inserted
  between "user clicked" and `IsBusy=true` would make Cancel uninvokable during it. Note this also
  means Cancel now stays *clickable* (though a harmless no-op, since `_cts` is already null by
  `finally`) throughout the post-cancel 2-second delay too.
- Cancellation itself has no intermediate state: `cts.Cancel()` only sets the token; the running
  `Task.Run` loop notices it at whatever granularity it happens to check
  (`cancellationToken.IsCancellationRequested`, or inside `CopyToAsync`, which also observes the
  token). Confirm this still holds for any new async step added inside `ArchiveAsync`/`ExtractAsync`.

---

## 3. Activity — Extract validation/foldering chain

Source read for this diagram: `ExtractWithSmartFolderingAsync` in
`src/Archiver.Core/Services/ZipArchiveService.cs`.

```mermaid
flowchart TD
    A0[allFileEntries = every non-dir ZIP entry] --> A1{"T-F05: options.SelectedEntryPaths<br/>set and non-empty?"}
    A1 -- no --> A2[fileEntries = allFileEntries;<br/>isSingleRootFolder/isSingleRootFile<br/>computed normally]
    A1 -- yes --> A3["fileEntries = allFileEntries filtered to<br/>the selected paths + anything nested<br/>under a selected folder path.<br/>isSingleRootFolder/isSingleRootFile forced<br/>false — actualDest = destDir always,<br/>no smart-foldering collapse for a subset"]
    A2 --> A4["Compression-bomb check (below) still sums<br/>allFileEntries, NOT the filtered subset —<br/>conservative: may over-warn, never under-warns.<br/>See DECISIONS.md's T-F05 entry."]
    A3 --> A4
    A4 --> A[For each entry in fileEntries<br/>— the (possibly filtered) set from A2/A3] --> C{isSingleRootFolder:<br/>strip leading segment}
    C -- stripped to empty --> Z[bytesRead += Length; no output]
    C -- non-empty, or not applicable --> D{"':' in entry name?<br/>(Alternate Data Stream) T-F38"}
    D -- yes --> S1[SkippedFiles += ADS reason]
    D -- no --> E{Last path segment matches<br/>CON/PRN/AUX/NUL/COM1-9/LPT1-9? T-F39}
    E -- yes --> S2[SkippedFiles += reserved name]
    E -- no --> F{Any char &lt; 0x20<br/>in entry name? T-F39}
    F -- yes --> S3[SkippedFiles += control chars]
    F -- no --> G{Resolved destFilePath does NOT<br/>start with fullTempDest?}
    G -- yes --> X["throw InvalidDataException<br/>→ propagates out of Task.Run in ExtractAsync<br/>→ caught there as ArchiveError<br/>→ WHOLE ARCHIVE fails, not just this entry"]
    G -- no --> H{PathContainsReparsePoint<br/>on any directory component? T-F37}
    H -- yes --> S4[SkippedFiles += reparse point]
    H -- no --> I{entry.CompressedLength&gt;0 &&<br/>entry.Length&gt;0 &&<br/>ratio &gt; 1000:1?}
    I -- yes --> S5[SkippedFiles += suspicious ratio]
    I -- no --> J{File.Exists at finalFilePath<br/>= actualDest + relativePath ?}
    J -- no --> K
    J -- "yes, onConflict==Skip" --> S6["bytesRead += Length; continue<br/>(no SkippedFiles entry recorded for this case)"]
    J -- "yes, onConflict==Rename" --> K2[destFilePath renamed via GetUniqueFilePath]
    J -- "yes, onConflict==Overwrite (or any other value)" --> K3["NO explicit branch — falls through<br/>unchanged to K with ORIGINAL destFilePath;<br/>actual overwrite happens later, only if the merge<br/>step's File.Move(overwrite:true) runs"]
    K2 --> K
    K3 --> K
    K[Extract entry to destFilePath in tempDest;<br/>TryPropagateMotw best-effort] --> L[bytesRead += entry.Length]
    S1 & S2 & S3 & S4 & S5 & S6 --> M{More entries?}
    L --> M
    Z --> M
    M -- yes --> A
    M -- no --> N["Commit: Directory.Move(tempDest→actualDest) if actualDest<br/>doesn't exist yet; ELSE merge each tempDest file into<br/>actualDest via File.Move(overwrite:true) — this is where<br/>an Overwrite-conflict entry actually overwrites"]
    N --> N2{"extractedCount == 0?<br/>(T-F87 — every entry hit S1-S6, nothing<br/>actually written to tempDest)"}
    N2 -- yes --> N3["SkippedFiles += whole-archive entry<br/>(Path == archivePath); caller does NOT<br/>add this archive to CreatedFiles"]
    N2 -- no --> O
    N3 --> O["ArchiveResult.Success = errors.Count == 0<br/>(ZipArchiveService.cs:449) — SkippedFiles is still NOT<br/>read in THIS computation, only in MainViewModel's<br/>DeleteAfterOperation cleanup gate (T-F87)"]
    O --> P{{"⚠ every per-entry branch (D,E,F,H,I,J-Skip) still feeds<br/>SkippedFiles, never errors — Success stays true for an<br/>all-entries-skipped archive, by design (T-F87 deliberately<br/>did not redefine Success — see DECISIONS.md). What N2/N3<br/>add: a per-archive signal (whole-archive SkippedFiles entry<br/>+ exclusion from CreatedFiles) so DeleteAfterOperation can<br/>no longer delete a source that was never extracted.<br/>T-F68 (fixed earlier): the shell path also shows a dialog<br/>for this case — see Program.cs's ShellResultPresenter."}}
```

**What this catches — a live finding, not a hypothetical:**
Every validation gate in this chain (ADS, reserved name, control chars, reparse, ZIP bomb,
`OnConflict=Skip`) routes to `SkippedFiles`, never `ArchiveError`. `ArchiveResult.Success` is
computed as `errors.Count == 0` (`ZipArchiveService.cs:449`) and does not look at `SkippedFiles`
at all. So an archive where *every* entry gets skipped reports `Success=true` with no real content
extracted besides an empty folder.

**Fixed downstream as T-F87 (node N2/N3 above):** this asymmetry became a real data-loss bug once
`DeleteAfterOperation` existed — `MainViewModel.ExtractAsync` deleted the source archive on
`Success=true` regardless of whether anything was actually extracted. Rather than redefine
`Success` (broad blast radius — every caller depends on its current meaning), the fix adds a
whole-archive `SkippedFiles` entry (`Path == archivePath`) when `extractedCount == 0`, and the
caller (`ZipArchiveService.ExtractAsync`) excludes that archive from `CreatedFiles`.
`MainViewModel.GetDeletableSources` then filters `DeleteAfterOperation`'s cleanup list against
`SkippedFiles` by full path — per-entry skips (S1-S6, relative entry names) never match a source's
full path, so only a genuine whole-archive skip blocks deletion. See `DECISIONS.md`'s "T-F87" entry.

**Not updated for `TestAsync` (T-F62), by decision:** `TestAsync` is a separate, structurally
simpler method — a flat per-archive loop with no foldering, no conflict handling, no path-escape
check, and no writes to disk at all — not a new branch inside
`ExtractWithSmartFolderingAsync`, so it isn't this diagram's subject. It does have its own
"silently dropped" shape worth naming: a path that is neither a ZIP nor a recognized foreign
archive format (`GetKnownArchiveReason` returns `null`) is skipped with no `SkippedFiles` entry
and no `ArchiveError` — mirroring `ExtractAsync`'s identical existing gap for the same input
shape (same `if (!IsZipFile(...)) { ...; if (reason is not null) ...; continue; }` pattern).
Not a new gap `TestAsync` introduces; not fixed here as it's `ExtractAsync`'s pre-existing
behavior, out of scope for T-F62.

The GUI path surfaces this correctly — `ShowOperationSummaryAsync` receives the full
`ArchiveResult` including `SkippedFiles`. **The shell path was fixed to match (T-F68, 2026-07-06):**
`Program.cs`'s `RunWithProgressWindowAsync` now calls `ShellResultPresenter.Classify(result)` and
shows a dedicated `MB_ICONWARNING` dialog ("N entries skipped: ...") whenever
`SkippedFiles.Count > 0` and there are no errors, instead of only checking `!result.Success ||
result.Errors.Count > 0`. `ArchiveResult.Success` itself is unchanged (still `errors.Count == 0`,
per node O above) — only the shell's dialog *trigger* was widened; see `DECISIONS.md`'s "T-F68"
entry for the two options considered and why widening the trigger (not `Success`) was chosen.

**Corrected in this redraw:** the `OnConflict` gate is not three parallel branches for three enum
values. The code is two sequential `if`s with no `else` (`ZipArchiveService.cs:597-609`) — `Skip`
and `Rename` are handled explicitly; `Overwrite` (and any future enum value) has no branch at all
and simply falls through to extraction with the original path, with the actual overwrite deferred
to the final merge step's `File.Move(overwrite: true)`. Drawing this as three clean branches in
the previous version hid that a new `ConflictBehavior` value added later would silently get
"extract unchanged" behavior unless a branch is added for it explicitly.

---

## 4. Component/Deployment — MSIX package & process boundaries

Source read for this diagram: `src/Archiver.App/Package.appxmanifest`.

```mermaid
flowchart TB
    subgraph MSIX["Pakko.msix — Identity: PavloRybchenko.Pakko"]
        subgraph AppApp["Application Id=App — EntryPoint=$targetentrypoint$ (WindowsAppSDK)"]
            App[Archiver.App.exe]
        end
        subgraph AppShell["Application Id=ShellHelper<br/>EntryPoint=Windows.FullTrustApplication<br/>AppListEntry=none"]
            Shell[Archiver.Shell.exe]
        end
        subgraph ComReg["com:Extension windows.comServer → com:SurrogateServer"]
            Dll["Archiver.ShellExtension.dll<br/>com:Class Id=1EABC7CE-20A4-48EE-A99F-43D4E0F58D6A<br/>ThreadingModel=STA"]
        end
    end

    Explorer[explorer.exe] -->|CoCreateInstance| Dllhost[dllhost.exe<br/>isolated COM surrogate process]
    Dllhost -->|loads| Dll
    Dll -->|"CreateProcess(Archiver.Shell.exe)<br/>⚠ ERROR_ACCESS_DENIED if not declared<br/>as its own Application entry"| Shell
    Shell -.->|"pakko://extract?files=... or<br/>pakko://archive?files=...<br/>(protocol activation, Open-UI flow only,<br/>not used by the context-menu path above)"| App
    Shell --> Core[Archiver.Core / ZipArchiveService]
    App --> Core
```

**What this catches:** any satellite EXE added later that is *not* given its own `<Application>`
entry with `EntryPoint="Windows.FullTrustApplication"` will build and run fine from Visual Studio
but fail with `ERROR_ACCESS_DENIED` the moment it's launched via `CreateProcess` from inside the
installed MSIX package — invisible until on-device testing. This is exactly the bug the
`ShellHelper` entry above was added to fix.

**Finding 1 (doc drift) — fixed 2026-07-06 as T-F69:** `ARCHITECTURE.md:259` had stated
*"Registered via `com:InProcessServer` in `Package.appxmanifest`"*, but the actual manifest
(`Package.appxmanifest:70-78`) uses `com:SurrogateServer`, matching `CLAUDE.md`'s own
"Correction — SurrogateServer" note in `DECISIONS.md`. `ARCHITECTURE.md` now says
`com:SurrogateServer` and its sub-command list was updated to include T-F63's new dialog commands.

---

## 5. Activity — tar.exe whole-archive pre-scan and extraction (T-F49)

Source read for this diagram: `ExtractSingleArchiveAsync`, `ScanForUnsafeEntriesAsync`,
`IsDangerousEntryName`, `EnumerateFilesGuarded` in
`src/Archiver.Core/Services/TarProcessService.cs:145-331`.

```mermaid
flowchart TD
    A[ExtractSingleArchiveAsync per archivePath] --> B["tar -tf archivePath (RunTarAsync)"]
    B -- "exit != 0" --> RejIO1["throw IOException(stdErr)<br/>quarantine never created —<br/>caught in ExtractAsync as ArchiveError"]
    B -- "exit 0" --> D{"for each listed name:<br/>IsDangerousEntryName?<br/>('..' segment, rooted path,<br/>ADS ':', reserved name, control char)"}
    D -- yes --> RejTar1["throw TarArchiveRejectedException<br/>WHOLE ARCHIVE rejected, quarantine never created"]
    D -- "no, all names clean" --> E["tar -tvf archivePath (RunTarAsync)"]
    E -- "exit != 0" --> RejIO1
    E -- "exit 0" --> F{"-tf line count ==<br/>-tvf line count?"}
    F -- no --> RejTar2["throw TarArchiveRejectedException<br/>('listing is inconsistent')"]
    F -- yes --> G{"for each -tvf line:<br/>char[0] == '-' or 'd'?"}
    G -- "no (l/h/b/c/p/s)" --> RejTar3["throw TarArchiveRejectedException<br/>('symlink, hardlink, device...')<br/>⚠ THIS is the gate that blocks the confirmed<br/>symlink-escape exploit — see DECISIONS.md's T-F49 entry"]
    G -- "yes, every entry '-' or 'd'" --> G2{"T-F05: options.SelectedEntryPaths<br/>set and non-empty?<br/>(gates D-G above already ran<br/>UNCONDITIONALLY — the pre-scan<br/>never branches on this)"}
    G2 -- no --> H["Directory.CreateDirectory(quarantineDir)<br/>tar -xf archivePath -C quarantineDir"]
    G2 -- yes --> G3["ExpandSelection(allNames, SelectedEntryPaths):<br/>each selected path → its exact -tf name<br/>(file or dir form) + every -tf name it's<br/>a '/'-prefix of (descendants) — built from<br/>the SAME name list gate D already validated,<br/>never a second '-tf' call"]
    G3 --> H2["Directory.CreateDirectory(quarantineDir)<br/>tar -xf archivePath -C quarantineDir &lt;expanded members&gt;"]
    H2 -- "exit != 0 (e.g. a stale/unmatched<br/>member name — 'Not found in archive')" --> RejIO2
    H2 -- "exit 0" --> I
    H -- "exit != 0" --> RejIO2["throw IOException(stdErr)<br/>→ finally still runs: quarantine deleted<br/>→ caught in ExtractAsync as ArchiveError"]
    H -- "exit 0" --> I["Directory.CreateDirectory(destDir)<br/>walk EnumerateFilesGuarded(quarantineDir)"]
    I --> J{"subdirectory hit during walk:<br/>IsReparsePoint?"}
    J -- yes --> K["⚠ silently NOT descended into —<br/>no SkippedFiles entry, no ArchiveError<br/>(see Finding below)"]
    J -- no --> L[yield each file in this directory]
    K --> Mloop{More entries?}
    L --> N{"File.Exists at<br/>destDir + relativePath?"}
    N -- no --> O
    N -- "yes, onConflict==Skip" --> P["SkippedFiles += 'already exists at destination'; continue"]
    N -- "yes, onConflict==Rename" --> O2["finalFilePath = GetUniqueFilePath(...)"]
    N -- "yes, onConflict==Overwrite (or any other value)" --> O3["NO explicit branch — falls through to O<br/>with the ORIGINAL finalFilePath;<br/>File.Move(overwrite:true) below does the actual overwrite<br/>(same asymmetry as diagram 3's ZIP OnConflict gate)"]
    O2 --> O
    O3 --> O
    O["File.Move(file, finalFilePath, overwrite:true)<br/>ArchiveEntrySecurity.TryPropagateMotw(archivePath, finalFilePath)"] --> Mloop
    P --> Mloop
    Mloop -- yes --> I
    Mloop -- no --> Q2{"totalFiles &gt; 0 &&<br/>extractedCount == 0?<br/>(T-F87 - every file hit P, nothing moved)"}
    Q2 -- yes --> Q3["SkippedFiles += whole-archive entry<br/>(Path == archivePath); caller does NOT<br/>add this archive to CreatedFiles"]
    Q2 -- no --> Q
    Q3 --> Q["return destDir<br/>(finally: quarantineDir deleted, success or failure)"]
    Q --> R{{"ArchiveResult.Success = errors.Count==0 (ExtractAsync); SkippedFiles still NOT read in this computation, only in MainViewModel's DeleteAfterOperation cleanup gate (T-F87)"}}
```

**What this catches — the confirmed exploit, and one new finding:**
- **Gate G is the load-bearing check.** It is the only thing standing between this pipeline and
  the reproduced symlink-escape exploit in `DECISIONS.md`'s T-F49 entry (a `link -> ..` symlink
  entry followed by `link/escaped.txt`, which made raw tar.exe write one directory level above
  the extraction root). Any future change that weakens gate G (e.g. widening the character
  whitelist, or trusting `-tvf`'s columns beyond character 0) reopens that exploit. Gates D and F
  run first but do not by themselves block a symlink entry — an entry named `link` with no `..`
  or rooted path in its name passes D cleanly; only G's type check catches it.
- **Whole-archive-reject, no per-entry fallback.** Unlike diagram 3's ZIP chain (where a bad
  entry is skipped and the rest of the archive still extracts), any rejection here
  (`RejTar1`/`RejTar2`/`RejTar3`) throws before `quarantineDir` is even created — the entire
  archive produces one `ArchiveError` and nothing is written. This is deliberate (see
  `DECISIONS.md`), but means a single overly-broad future name/type check would silently reject
  entire legitimate archives rather than just skipping one entry.
- **New finding (node K): a reparse-point subdirectory hit during the post-extraction walk is
  silently dropped** — `EnumerateFilesGuarded` simply doesn't push it onto its traversal stack,
  recording neither a `SkippedFiles` entry nor an `ArchiveError`. Currently unreachable in normal
  operation, since gate G already rejects any archive containing a symlink entry before `-xf`
  ever runs — this path only matters if gate G is ever weakened, or in the already-documented
  TOCTOU gap between the scan pass and `-xf` (archive modified between the two). Flagged per this
  file's Ground Truth Rule rather than silently patched; not fixed as part of T-F49 since it's
  currently dead code, not a live gap — worth a one-line `SkippedFiles` addition if gate G's
  guarantees are ever loosened.
- **Same `OnConflict` asymmetry as diagram 3:** `Overwrite` (and any future `ConflictBehavior`
  value) has no explicit branch and falls through to the unconditional `File.Move(overwrite:
  true)` — identical shape to `ZipArchiveService`'s gate, confirmed by reading
  `TarProcessService.cs:176-190` directly rather than assuming parity with diagram 3.
- **New (T-F05, node G2/G3): the archive browser's "Extract selected" narrows what `-xf` extracts,
  but never what the pre-scan validates.** Gates D through G run unconditionally, exactly as
  before `SelectedEntryPaths` existed — the branch at G2 only changes the member-argument list
  passed to `-xf`, never whether the whole-archive scan runs. `ExpandSelection` builds that list
  from `allNames` (the same list gate D already validated), so a stale/mismatched selected path
  fails the entire `-xf` call with a real tar.exe error rather than silently extracting nothing —
  see `DECISIONS.md`'s T-F05 entry for the empirical spike confirming tar.exe's exact
  member-matching and directory-auto-recursion behavior this relies on.
- **Same `Success`/`SkippedFiles` asymmetry as diagram 3, fixed downstream the same way (T-F87,
  nodes Q2/Q3):** an extraction where every file was skipped (e.g. `OnConflict=Skip` and every
  entry already exists at the destination) still reports `Success=true` — `Success` itself was
  deliberately left as `errors.Count==0` (see `DECISIONS.md`'s "T-F87" entry for why). What Q2/Q3
  add: a whole-archive `SkippedFiles` entry (`Path == archivePath`) when nothing was actually
  moved, and exclusion of that archive from `CreatedFiles`, giving `MainViewModel`'s
  `GetDeletableSources` the same per-archive signal it uses for the ZIP path so
  `DeleteAfterOperation` can't delete a source that was never extracted.

---

## Findings summary (surfaced while drafting/redrawing 2026-07-05, all three since resolved)

1. **`ARCHITECTURE.md:259` stale** — said `com:InProcessServer`, actual manifest and
   `DECISIONS.md` say `com:SurrogateServer`. Tracked as **T-F69** — fixed 2026-07-06.
2. **Possible silent-empty-extract bug** — `ArchiveResult.Success` ignores `SkippedFiles`; the
   shell path (`Program.cs:235`) only checks `Errors`, so an all-skipped shell extraction shows
   no dialog at all. Tracked as **T-F68** — fixed 2026-07-06 (see diagram 3's note below and
   `DECISIONS.md`).
3. **`IsBusy` vs. status-text asymmetry** — after a cancelled operation, `IsBusy` was already
   `false` throughout the fixed 2-second `StatusCancelled` display, while after a completed/errored
   operation `IsBusy` stayed `true` for as long as the summary/error dialog was open. Tracked as
   **T-F70** — decided (align, not document) and fixed 2026-07-06; see diagram 2 above and
   `DECISIONS.md`.

## Findings summary (surfaced while drafting diagram 5, 2026-07-07)

4. **Reparse-point subdirectory silently dropped during `TarProcessService`'s post-extraction
   walk** — no `SkippedFiles` entry, no `ArchiveError`; see diagram 5's node K and its note above.
   Not tracked as a `T-Fxx` and not fixed — currently dead code (gate G already rejects any
   archive containing a symlink entry before this walk can run), so there is nothing live to fix
   yet. Revisit if gate G's guarantees are ever loosened.
