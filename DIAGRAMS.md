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
    Explorer->>EDC: GetState(psia) → ECS_ENABLED iff AnyPathIsZip(paths), else ECS_HIDDEN<br/>(T-F63: same gate as TC, NOT the stricter AllPathsAreZip EH/EF use)
    Explorer->>EH: GetState(psia) → ECS_ENABLED iff AllPathsAreZip(paths), else ECS_HIDDEN
    Explorer->>EF: GetState(psia) → ECS_ENABLED iff AllPathsAreZip(paths), else ECS_HIDDEN
    Explorer->>CDC: GetState(psia) → always ECS_ENABLED (T-F63: shown for any selection,<br/>unlike AC below — archiving a .zip into a new .zip via the dialog is valid)
    Explorer->>AC: GetState(psia) → ECS_HIDDEN iff AllPathsAreZip(paths), else ECS_ENABLED<br/>(condition is INVERTED vs. EH/EF)
    Explorer->>AC: GetTitle(psia) → BuildAddToArchiveTitle(paths)<br/>dynamic "Add to <name>.zip", truncated middle if >40 chars
    Explorer->>TC: GetState(psia) → ECS_ENABLED iff AnyPathIsZip(paths), else ECS_HIDDEN<br/>(T-F62: AnyPathIsZip, NOT AllPathsAreZip — shows on a mixed selection too)
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

---

## 2. State — Operation lifecycle (`MainViewModel`)

Source read for this diagram: `src/Archiver.App/ViewModels/MainViewModel.cs`
(`ArchiveAsync`/`ExtractAsync`/`Cancel`, lines 228–437). Both methods have the identical
try/catch/finally shape; the diagram applies to either.

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
`src/Archiver.Core/Services/ZipArchiveService.cs:463-668`.

```mermaid
flowchart TD
    A[For each ZIP file entry<br/>fileEntries excludes dir markers ending '/'] --> C{isSingleRootFolder:<br/>strip leading segment}
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
    N --> O["ArchiveResult.Success = errors.Count == 0<br/>(ZipArchiveService.cs:449) — SkippedFiles is NOT<br/>read anywhere in this computation"]
    O --> P{{"⚠ every branch reachable in normal operation<br/>(D,E,F,H,I,J-Skip) feeds SkippedFiles, never errors.<br/>Only G (path escape) produces an ArchiveError.<br/>So: all-entries-skipped ⇒ Success=true,<br/>an (empty or near-empty) folder is created.<br/>T-F68 (fixed): the shell path now shows a dialog for<br/>this case too — see Program.cs's ShellResultPresenter,<br/>not a change to Success itself, still computed as drawn here."}}
```

**What this catches — a live finding, not a hypothetical:**
Every validation gate in this chain (ADS, reserved name, control chars, reparse, ZIP bomb,
`OnConflict=Skip`) routes to `SkippedFiles`, never `ArchiveError`. `ArchiveResult.Success` is
computed as `errors.Count == 0` (`ZipArchiveService.cs:449`) and does not look at `SkippedFiles`
at all. So an archive where *every* entry gets skipped reports `Success=true` with no real content
extracted besides an empty folder.

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
