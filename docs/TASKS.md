# TASKS.md — Active and Future Tasks

> Open work only: the v1.10.0 wave first, then the future tasks. Finished tasks live in
> [`TASKS_DONE.md`](TASKS_DONE.md); each graduation below names its section there.
>
> **Graduated 2026-11-06 (after v1.9.0, docs audit):** section "Graduated 2026-11-06 (after v1.9.0)" of
> [`TASKS_DONE.md`](TASKS_DONE.md): the v1.9.0 wave plan, the old v1.1 state, the v1.2 OS note, the
> NanaZip menu review, the intros of the test-coverage audit (T-F174-T-F186) and of ZIP password
> support (T-F188-T-F194), the pre-2026-07-12 T-F05 and T-F09 entries, T-F114, T-F430, T-F431, T-F433, T-F436, T-F438, T-F399.
>
> **Graduated 2026-09-30 (after v1.6.0):** these completed tasks now live in [`TASKS_DONE.md`](TASKS_DONE.md), section "Graduated 2026-09-30": T-F05, T-F116, T-F124, T-F125, T-F126, T-F129, T-F35, T-F51, T-F131, T-F133, T-F134, T-F136, T-F137, T-F138, T-F142, T-F143, T-F147, T-F148, T-F149, T-F159, T-F160, T-F164, T-F171, T-F172, T-F195, T-F196, T-F197, T-F198, T-F199, T-F200, T-F201, T-F203, T-F204, T-F205, T-F207, T-F208, T-F209, T-F210, T-F212, T-F213, T-F214, T-F215, T-F216, T-F217, T-F218, T-F219, T-F222, T-F224, T-F227, T-F228, T-F229, T-F230, T-F232, T-F233, T-F234, T-F237, T-F238, T-F239, T-F240, T-F242, T-F248, T-F249, T-F250, T-F252, T-F253, T-F255, T-F256, T-F257, T-F258, T-F259, T-F262, T-F264, T-F265, T-F267, T-F269, T-F270, T-F271, T-F272, T-F283, T-F287, T-F289, T-F273, T-F274, T-F276, T-F277, T-F278, T-F279, T-F223, T-F305, T-F174, T-F175, T-F176, T-F177, T-F178, T-F179, T-F180, T-F181, T-F182, T-F183, T-F184, T-F185, T-F186, T-F187, T-F188, T-F189, T-F190, T-F191, T-F193.
>
> **Graduated 2026-10-05 (after v1.7.0):** section "Graduated 2026-10-05" of [`TASKS_DONE.md`](TASKS_DONE.md): T-F09, T-F128, T-F48, T-F89, T-F111, T-F112, T-F115, T-F146, T-F206, T-F211, T-F220, T-F221, T-F225, T-F235, T-F236, T-F243, T-F244, T-F245, T-F246, T-F247, T-F251, T-F260, T-F263, T-F266, T-F268, T-F284, T-F288, T-F286, T-F281, T-F282, T-F291, T-F292, T-F293, T-F294, T-F295, T-F296, T-F297, T-F298, T-F299, T-F300, T-F301, T-F302, T-F303, T-F304, T-F306, T-F307, T-F308, T-F309, T-F312, T-F310, T-F192, T-F194, T-F321, T-F323, T-F330.
>
> **Graduated 2026-10-06 (after v1.7.1):** section "Graduated 2026-10-06" of [`TASKS_DONE.md`](TASKS_DONE.md): T-F231, T-F241, T-F280, T-F313, T-F316, T-F322, T-F325, T-F333, T-F335, T-F337, T-F338
>
> **Graduated 2026-10-06 (T-F336: cancelled, superseded or shipped long ago):** section "Graduated 2026-10-06 (T-F336)" of [`TASKS_DONE.md`](TASKS_DONE.md): T-F01, T-F04, T-F07, T-F08, T-F13, T-F15, T-F33, T-F34, T-F36, T-F41, T-F42, T-F43, T-F96

---

## ⚠ Agent Rules — Read Before Every Task

These rules apply to ALL tasks. Violating them = task is NOT complete.

**Completion rules:**
- NEVER mark `[x]` unless every single acceptance criterion is checked `[x]`
- `[~]` means partially complete — UI done but logic missing, or logic done but untested
- A task with ANY `[ ]` criterion must stay `[ ]` or `[~]` — never `[x]`

**Testing rules:**
- Test-run commands and when to run the Slow filter are `CLAUDE.md`'s Hard Constraints — the
  canonical copy; don't restate them here.
- If tests fail → fix before marking anything complete
- Every new behavior in `ZipArchiveService` needs at least one test

**UI vs Logic rules:**
- UI-only implementation = `[~]` not `[x]`
- If a task touches both XAML and a service, BOTH must be done before `[x]`
- Options passed from ViewModel to service must actually be READ and ACTED ON in the service

**Scope rules — which options apply to which action:**
- Archive-only options (Name, Mode, Compression, DeleteSourceFiles) → `ArchiveOptions` only
- Extract-only options (DeleteArchiveAfterExtraction) → `ExtractOptions` only
- Shared options (Destination, OnConflict, OpenDestinationFolder) → both

---

## v1.10.0 wave — bug fixes and the progress on the taskbar and tray (planned 2026-10-31)

The next minor release (the user, 2026-10-31): the bugs left open after v1.9.0, the SonarCloud
findings on `main`, and T-F432 (the user put it in this release on 2026-10-29 and asked again on
2026-10-31 for the tray icon too). Same rules as the v1.9.0 wave: each stream on its own branch from
`main` and its own PR; sandbox, integration and subprocess tests and the full suite one at a time;
TASKS, TASKS_DONE, CHANGELOG, SPEC and CLAUDE.md edited by the integrating session only. Strings in
37 locales: one stream at a time per resource set (App `.resw`: C then D; Shell `.resx`: C).

| Stream | Tasks | Area | Gate |
|---|---|---|---|
| A text boundaries, analyzer | T-F441 (one owner per encoding boundary, names and paths, with its control test), T-F424 as T-F441's first case, T-F440 (SonarCloud findings, the `S3903` bug first), T-F455, T-F454, T-F453 (tar folder bugs on `main` found by T-F443; added 2026-11-06, the agent) | `Sandbox/Tar*Encoding`, `Zip/ZipNameCodePages`, `StdinPathList`, `CliConsoleCharset`, `BareGzip*`, `Program.cs`, tests | T-F441: research, plan mode + advisor at both ends; T-F440: tests first |
| B sandbox launcher, AMSI tests | T-F428 (cancel kills the child itself), T-F427 (AMSI not ready at scan time), T-F444 (another antivirus or none, on the VM; added 2026-10-31, the user), T-F446 (AMSI "blocked by admin" reads as Clean), T-F447 (more providers on the VM; added 2026-11-03, the user), T-F448 (Trend Micro's provider answers Clean for EICAR), T-F449 (an empty scan reports Clean; added 2026-11-04, the user), T-F450 (a timed-out tar version probe reads as an old tar; added 2026-11-04, the user), T-F460 (a tar folder ending in a space or a dot scans Clean; P1, added 2026-11-06, the agent) | `SandboxedProcessLauncher`, `AntivirusScanService` tests | T-F428: plan mode + advisor at both ends (sandbox code) |
| C after the operation | T-F434 (recycle each clean source), T-F435 (a reason per kept item), T-F437 (unreadable path list) | `SourceRecycler`, the not-deleted dialog, `Archiver.Shell` messages, `SPEC.md` rule | T-F434: plan mode (an irreversible action on user data) |
| D progress | T-F432 (taskbar button and tray icon), T-F439 (window floor on a monitor move) | App `MainWindow`, tray icon, `Archiver.OperationUi`, `WindowFloor` | T-F432: plan mode (COM under Native AOT) |
| F winget | T-F317's outward step: `PavloRybchenko.PakkoCLI` in `microsoft/winget-pkgs` (the user, 2026-10-31) | `scripts/New-WingetManifest.ps1`, the winget-pkgs PR | after the v1.10.0 release |
| G long-open umbrellas (added 2026-10-31, the agent's pick) | T-F254 (regional display language: pack and sign-out on the PakkoW10 VM, not the user's machine), T-F91 (`ru-RU` falls back to en-US, on the VM; the native-speaker criterion replaced by an agent back-translation pass, since there are no native reviewers, the user's standing word), T-F328's leftovers (quote, dash and apostrophe normalisation), T-F202's light theme, en-US and tray (through the `windows` MCP, as `APP-013` and `EXP-008` already switched theme and language), T-F329 part 5 (Store screenshots retaken after D changes the window) | VM, `.resw`/`.resx`/`Localization.cpp` (after C and D's strings), `docs/assets/store/` | after C and D merge |
| H formats tar.exe reads but Pakko does not offer (added 2026-10-31, the user) | T-F442 (measure first, per tar.exe build; then detector, policy names, capability snapshot, Explorer menu, file associations) | `ArchiveFormatDetector`, `ArchiveFormatPolicy`, `TarCapabilitySnapshot`, `ShellExtUtils.cpp`, the manifest | measurement first; plan mode + advisor at both ends (new untrusted-input formats, the manifest) |
| I opened during the wave (added 2026-11-06, the user: "the tasks opened during it, small fixes where possible") | First, from the root-cause review (the user, 2026-11-06: "in this wave"): T-F462 (Clean only with every file scanned, P1), T-F463 (one owner for quarantine disk access), T-F464 (the quarantine as source of truth, research). From T-F464's research (2026-11-07): T-F465 (tar.exe's listing decoded in the wrong page, P1), T-F466 (NFD names refused), T-F467 (one encoding provider), T-F468 (`archiveint.dll` research). Moved in by the user (2026-11-07): T-F470 (a sandboxed helper over Windows' own `archiveint.dll`, the name source; plan mode + advisor at both ends + a fresh reviewer). Small fixes: T-F456 (a flaky OperationUi test), T-F457 items 3-4 (`.tlz` as tar.lzma; `-stxudf`; done in #169), T-F453 (two true roots under one best-fit name), T-F452 (its own message for a header mismatch, 37 locales). Then: T-F461 (the folder shapes still refused; measure on 3.5.2 and in the sandbox first), T-F457 items 1, 2 and 5 (decide or measure; 1 and 2 done 2026-11-08), T-F445 (pool threads per launch; measure first), T-F459 (`TESTING.md` regrouped, docs only; done 2026-11-07) | as each task says | T-F445: plan mode + advisor at both ends (sandbox code); each fix tests first, both tar builds where tar.exe decides |
| E on device, release | `APP-027`, `APP-028`, `APP-030`, `APP-014`, `OPW-*`, the new taskbar/tray rows, the release-tier smoke, `STO-005` as a real Store update | `docs/SMOKE.md`, `scripts/smoke/` | after A-D merge |

Decisions taken with the plan (the agent, 2026-10-31): T-F432 covers the App's taskbar button, the
App's tray icon and the Explorer operation window's taskbar button (the tray icon is the App's only;
the operation window has none). The taskbar button and the tray icon follow the whole batch, as
Explorer's own copy does; the window's text keeps the current archive's speed and time left
(T-F431). Paused (yellow) while a prompt waits, error (red) when the run ends with a failure,
cleared when the summary shows. T-F434: recycle each fully processed, unchanged source; a skipped
source keeps only itself (written into `docs/SPEC.md`), not the others. T-F393 stays deferred
(25 of 25 tries opened the dialog).

Stream I goes before E, so the device checks see its fixes (the agent, 2026-11-06). Order: A, B, C and D's code in parallel (T-F428, T-F434 and T-F432 after their plans); **no test
runs, smoke or device checks while the user's PC is busy** (the user, 2026-10-31: the smoke runs
later, after the fixes); then E on the device, the user's elevated checks batched right before the
release, the release (`.claude/rules/ci.md`) and the Store submission with the sv-se lists typed into
Partner Center's form (DECISIONS "v1.9.0 - the Store install").

Stream F (checked 2026-10-31): winget-pkgs #446296 was closed by us as superseded by #449503
(1.7.2), which is open since 2026-10-09 with `Validation-Completed`, `Azure-Pipeline-Passed` and the
CLA signed by both accounts; it waits only for a community moderator. After the v1.10.0 tag:
`New-WingetManifest.ps1` for the release's CLI zips, `winget validate`, `winget install --manifest`
with `a`/`t`/`l`/`x`, then the new version replaces 1.7.2 in that PR (or a new PR superseding it)
from `pakkoapp-oss`, and the moderators asked for a review. Graduates when `winget install
PavloRybchenko.PakkoCLI` works from the public source; later versions automated from `build.yml`
(T-F317 Part A) once the package exists.

**Deferred past 1.10.0:** T-F359 (no spinning disk), T-F332's super hero art (the user's image),
T-F393 (no repro), T-F318 (waits for .NET 11), and the future items that are not fixes (T-F02,
T-F10, T-F121, T-F127, T-F132, T-F144, T-F152). T-F254, T-F91 and T-F202's leftovers moved into
stream G (2026-10-31): the VM and the `windows` MCP replace the user's machine and hands.

---

## Future Tasks

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

Separate window for archive configuration instead of inline controls.

---

### T-F119 — Archiver.CLI PATH Distribution (winget/scoop manifest)
- **Folded into T-F317 (2026-10-03, user):** the winget part is T-F317's first half; `scoop` stays
  out of scope until asked for. The text below is kept as background.
- [ ] **Status:** future — flagged 2026-07-18 during T-F116's naming/distribution discussion. See
      `CLI.md`'s Distribution section and `DECISIONS.md`'s T-F116 follow-up entry.
- **Depends on:** T-F09 (CLI Core)

**What:** `pakko.exe` currently ships only as a manually-downloaded, manually-unzipped artifact —
confirmed (via research into ripgrep/fd/bat) that this matches the norm for zip-distributed CLI
tools, but it means there is no "install once, available everywhere" path today. A `winget`
manifest (Microsoft's own package manager, pre-installed on Windows 10 1709+/Windows 11) is the
natural next step — its `Portable` installer type registers the exe in a PATH-managed `Links`
folder without needing a custom installer or elevated MSI. `scoop` is a plausible second target
for the same reason (also PATH-shim-based, no admin rights needed) but lower priority.

**Acceptance criteria:**
- [ ] `winget` manifest (`pakkoapp.pakko` or similar id — check availability) targeting the
      `win-x64`/`win-arm64` zips already produced by `scripts/Publish-Cli.ps1`, using the
      `Portable` installer type
- [ ] Manifest validated via `winget validate` and a real local install
      (`winget install --manifest <path>`) confirming `pakko` resolves from a fresh terminal with
      no manual PATH edit
- [ ] Submission process to `microsoft/winget-pkgs` documented in `scripts/README.md` (this is a
      recurring release step, not a one-time action — new manifest version needed per release)
- [ ] `CLI.md`'s Distribution section updated once this ships, replacing the "not yet scheduled"
      note

---

### T-F121 — Explore true zero-copy streaming for `-so` extraction output
- [ ] **Status:** future, low priority — exploratory only, no committed design yet. Raised
      2026-07-18 when the user asked whether T-F116's `-si`/`-so` could avoid buffering entirely;
      confirmed the current design is deliberately buffer-then-proceed (temp-file staged), not a
      technical gap — see `CLI.md`'s "Stdin/stdout streaming" section and `DECISIONS.md`'s T-F116
      entry. `-si` (reading a ZIP) can't be made zero-copy at all — `ZipArchive` needs a seekable
      stream since the central directory sits at the end of the file, and `TarSandboxedService`'s
      T-F49 pre-scan is a deliberate security requirement (full archive on disk before extraction
      runs), not an implementation shortcut. The one side worth a second look is `-so` on `x`:
      instead of extracting to a private temp folder then copying the one result file to stdout,
      stream bytes to stdout as they're produced during extraction.
- **Depends on:** T-F116 (CLI stdin/stdout streaming)

**Open questions to resolve before this becomes a real design, not yet answered:**
- [ ] How to preserve the "never emit partial bytes on failure" guarantee without a completed file
      to check first — today a failed operation writes nothing to stdout at all
      (`CliStreamStaging`'s current design's central property)
- [ ] How to know upfront that extraction resolves to exactly one file (today discovered by
      enumerating the temp folder afterward) without either pre-listing the archive first or
      accepting that a multi-file case fails only after streaming has already started
- [ ] Whether tar-family extraction (subprocess + quarantine ACL, not an in-process stream) can
      participate in this at all, or whether true streaming would end up ZIP-only — a format-
      dependent capability split that would need to be documented clearly, not silently assumed
- [ ] Whether the payoff (avoiding transient temp-disk use for one archive's worth of bytes) is
      worth the added failure-mode complexity, given `-so` archives are already expected to fit on
      disk once (the archive itself gets written to a temp file either way in the current design)

**Acceptance criteria (once a design is agreed — not before):**
- [ ] A short design note in `DECISIONS.md` before any implementation, per this project's
      pre-implementation-research convention
- [ ] `Archiver.Core.PerformanceTests`-style before/after evidence that it's actually faster or
      lower-overhead for a real large single-file extraction, not just theoretically cleaner
- [ ] Existing `-so`/`-si` test coverage (T-F116, `Subprocess/CliSubprocessTests.cs`) stays green,
      plus new tests for the harder failure-mode edge cases raised above

---

### T-F127 — Wikipedia page (Ukrainian + English)
- [ ] **Status:** future, added 2026-07-19 at the user's explicit request. **Real risk flagged
      before any work starts, per this project's pre-implementation-research norm:** checked
      Wikipedia's actual notability guideline for software (`WP:NSOFT`/
      `Wikipedia:Notability (software)`) — it requires significant coverage of the *software
      itself* in independent, reliable secondary sources (press reviews, printed manuals, or
      recognized historical/technical significance), not just an active public GitHub repo. As of
      2026-07-19 Pakko has no independent press coverage at all (GitHub-only releases for early
      testers, no reviews, no news mentions found) — an article created now would very likely be
      speedy-deleted (`WP:CSD` A7) or fail an Articles-for-Deletion discussion for lack of
      notability, on both `en.wikipedia.org` and `uk.wikipedia.org` (Ukrainian Wikipedia applies
      an equivalent standard). This is not a formatting/writing problem, it's an eligibility
      problem — no amount of good prose fixes it.
- **Depends on:** none technically, but see the note above — realistically blocked on Pakko
      accumulating independent secondary-source coverage first (e.g. a tech-press review, a
      notable government/enterprise adoption case covered by a third party). **Not blocked on**
      T-F124/T-F10 (code signing) or the Microsoft Store listing — those aren't Wikipedia
      notability sources either.

**Scope (once notability is realistically met):**
- Draft the article once via a shared source (English first, then a Ukrainian translation, or
  vice versa — not two independently-drafted articles, to avoid the two language versions
  drifting on basic facts like license/version/feature list)
- Disclose the conflict of interest: per `WP:COI`, the project's own maintainer writing about
  their own project is a connected contribution — must be disclosed on the article's talk page
  (`{{connected contributor}}` template) and the editor's own user page, and the recommended path
  is submitting via Articles for Creation (`WP:AFC`) as a draft for independent review, not
  publishing directly to mainspace
- Cite only independent secondary sources for notability-relevant claims — this repo's own
  `README.md`/`SECURITY.md`/`TASKS.md` are primary sources and don't establish notability, only
  factual detail once notability is otherwise established

**Acceptance criteria:**
- [x] Real notability check redone at implementation time (sources may exist by then that don't
      today) — record what was found, don't assume the 2026-07-19 "not yet notable" finding still
      holds without rechecking. **Rechecked 2026-07-19 (same day):** live web search for "Pakko"
      alongside archiver/WinUI/zip/GitHub terms returned zero results referencing this project at
      all — no press coverage, no reviews, no third-party mentions of any kind exist yet. Same
      conclusion as the original finding above; task stays blocked, not started.
- [ ] If proceeding: COI disclosed per `WP:COI` before any mainspace edit
- [ ] English draft submitted via AfC (or mainspace, if a competent Wikipedia editor advises
      notability is clearly met and AfC is unnecessary)
- [ ] Ukrainian draft submitted via Ukrainian Wikipedia's equivalent process
- [ ] Both articles survive their respective new-article review process without deletion

---

### T-F10 — Code Signing
- [ ] **Status:** future. **SignPath Foundation eligibility resolved 2026-07-23: rejected** (see
      T-F124's Status and `DECISIONS.md`'s T-F124 rejection entry) — insufficient public-visibility
      signals, not a quality judgment; reapplication invited once Pakko has broader external
      recognition (stars/forks/contributors/articles). **Phase 1 below as originally written
      (SignPath Foundation as "the chosen path") is no longer the active plan** — next real
      decision needed is which fallback from the cert-options table to pursue now (paid SignPath
      subscription, an OV cert, or staying self-signed for internal Ukrainian-gov distribution
      while continuing to grow public visibility toward a future SignPath Foundation reapplication)
      **Decided 2026-07-23 (user):** stay self-signed for internal/Ukrainian-gov distribution for
      now — no paid SignPath subscription or OV cert purchase yet. Revisit once T-F129 (Microsoft
      Store submission) actually resolves: a live Store listing is itself a real public-visibility/
      institutional-backing signal, and is the intended trigger to reapply to SignPath Foundation
      rather than paying immediately. T-F10 stays blocked/dormant on that outcome, not actively
      worked until then. **Trigger condition met 2026-08-04** — T-F129 is now `[x]` done and the
      Store listing is live (see its entry above); the SignPath Foundation reapplication decision
      itself is still open and needs the user's call, not auto-started here.
      Scope explicitly includes `Archiver.CLI`'s `pakko.exe`/`pakko-win-*.zip`
      (T-F09/T-F116, added 2026-07-18) — not just the MSIX. `pakko.exe` is downloaded and run
      standalone, outside any package-manager trust chain, so it hits the exact SmartScreen/
      AppLocker friction described below on its own, independent of whether the MSIX is signed.
      Don't scope this task down to "just the MSIX" without an explicit decision to split it.

**Why critical for target audience:** government/defense environments often block unsigned executables via AppLocker/WDAC. Unsigned MSIX triggers SmartScreen.

**Two levels, two different signing mechanisms — don't conflate them:**
- MSIX package signature — required for sideload installs, covers `Archiver.App`'s package as a
  whole (and everything bundled inside it, including the satellite EXEs `Archiver.Shell.exe`/
  `Archiver.ShellExtension.dll`)
- Authenticode on standalone binaries — visible in file Properties → Digital Signatures; this is
  the mechanism that matters for `pakko.exe`, since it's downloaded and run **outside** the MSIX/
  package trust boundary entirely (T-F09/T-F116)

**Certificate options — researched 2026-07-18 (Microsoft Learn's "Code signing options for Windows
app developers," updated 2026-04-20 — verified current, not from memory) after the user asked
whether free Microsoft Store signing could also cover `pakko.exe`. It cannot: Store re-signing is
free but applies **only to an MSIX package actually submitted through the Store** — a standalone
loose `.exe` distributed via GitHub Releases (never submitted as a package) is never touched by
it. Submitting `pakko.exe` to the Store via the MSI/EXE-installer path (a separate path from MSIX)
doesn't help either — Microsoft explicitly does not re-sign Win32 MSI/EXE installer submissions;
you're required to already hold your own Authenticode cert before submitting.**

| Option | Cost | Availability | SmartScreen | Notes |
|--------|------|------|------|------|
| **Microsoft Store (MSIX)** | Free | Worldwide | No warnings | Already covers `Archiver.App`'s MSIX if/when submitted — doesn't touch `pakko.exe` |
| **SignPath Foundation** | **Free**, for qualifying open-source projects | No geographic restriction found | Reputation builds over time like a paid cert, but the cert itself is free | **Applied 2026-07-23, rejected** (T-F124) — real eligibility bar is public-visibility signals (stars/forks/contributors/external articles/institutional backing), not just license+CI+privacy-policy checkboxes as the pre-application gap analysis assumed. Reapply once those signals grow. |
| SignPath (paid subscription) | Paid, tier per `docs.signpath.io/change-subscription` | Same infra as the Foundation program | Same reputation-building model | Offered directly in SignPath's rejection email as the immediate alternative to waiting for Foundation eligibility — pricing not yet checked against this project's budget |
| Azure Artifact Signing (formerly "Trusted Signing") | ~$9.99/mo | Individuals: **USA/Canada only**. Organizations: also EU/UK | Reputation builds over time | Blocks an individual developer submitting from Ukraine — would need to register as an org in an eligible region, or use a different option |
| OV certificate (DigiCert, Sectigo, etc.) | $150–300/yr | Worldwide | Reputation builds over time | Fallback if SignPath Foundation eligibility doesn't pan out in practice |
| EV certificate | $400+/yr | Worldwide | **No longer instant** — Microsoft removed EV's SmartScreen-bypass-on-first-download behavior in 2024; EV now builds reputation the same way OV does | Not worth the premium anymore, purely for SmartScreen purposes (older docs/advice claiming "immediate trust" are stale) |
| Self-signed | Free | — | Blocks install for public users; fine for enterprise-managed trust | For Ukrainian government deployment specifically: self-signed with the root cert distributed via Group Policy remains viable for *internal* rollout, independent of whatever's chosen for public GitHub distribution |
| Sigstore / Cosign | Free | Worldwide | **None — does not apply** | **Not a substitute for the above, added 2026-07-19 per user request for comparison.** Confirmed via research (not assumed): Sigstore's Fulcio CA is not in the Microsoft Trusted Root Program, so a Sigstore/Cosign signature is not an Authenticode signature — Windows SmartScreen/AppLocker/WDAC never see it and a `pakko.exe` signed only this way still shows "Unknown Publisher." `cosign sign-blob` also produces a detached signature bundle, not a PKCS#7 signature embedded in the PE file the way `signtool`/SignPath do. Solves a genuinely different problem (supply-chain provenance/attestation — proving a given binary was really built by this repo's own CI, verifiable via `cosign verify-blob`/`gh attestation verify`) than the one T-F10 exists to solve (SmartScreen warnings blocking install for a government/defense audience). **Worth adding anyway, as a free complement, not a replacement:** GitHub Actions' built-in `actions/attest-build-provenance` (public repos, free, Sigstore-backed, near-zero setup on top of the existing `build.yml`) gives SLSA Build Level 2 provenance attestations for the MSIX and `pakko.exe` — fits Pakko's whole auditability/transparency positioning (`SECURITY.md`) as a nice-to-have, independent of and in addition to whichever Authenticode option above is chosen. Tracked as T-F125, not a candidate for T-F10's actual cert decision. |

**Working plan — two phases, researched 2026-07-18 after the user asked whether one SignPath
certificate could cover both the MSIX and `pakko.exe`, and how that combines with an eventual
Store submission:**

**Phase 1 (now — MSIX and CLI both ship via direct GitHub download, no Store submission yet):**
SignPath Foundation explicitly supports EXE/DLL **and MSIX/AppX** ("deep signing") through the
same account/pipeline, per their own changelog — one application covers both `pakko.exe` and the
MSIX for as long as the MSIX keeps shipping outside the Store. **Real gotcha, confirmed via
research, not assumed:** an MSIX's `Package.appxmanifest`'s `<Identity Publisher="CN=...">` must
match the signing certificate's Subject exactly — SignPath's own error messages specifically flag
a mismatch here. Today that field holds the local self-signed dev cert's CN (`Setup-DevCert.ps1`);
switching to SignPath means updating `Identity Publisher` to SignPath's issued cert's Subject, and
re-pointing `Deploy.ps1`'s signing step from local `SignTool` + dev cert to SignPath's CI
integration (their PowerShell module, called from GitHub Actions or wherever the release build
runs) instead of a locally-installed cert.

**Phase 2 (later — if/when the MSIX is actually submitted to the Store, per the roadmap's "planned
once T-F51/GPO is done"):** confirmed via Microsoft's own "Publish your first Windows app" guide —
an MSIX submitted to the Store needs **no CA-trusted signature at all** to be accepted; Microsoft
re-signs it with its own certificate after certification, regardless of what it was built/signed
with beforehand (even the existing self-signed dev cert is fine for the submission itself). What
Store submission *does* require is a **separate, later** manifest change: Partner Center assigns
its own Publisher ID (a different GUID-format CN) that `Identity Publisher` must match *at
submission time* — unrelated to whatever cert SignPath issued in Phase 1. Once certification
passes, SignPath is no longer needed for the MSIX specifically (Store re-signs every future
release automatically) — but `pakko.exe` keeps needing its own binary signing indefinitely, since
a portable CLI exe isn't something that goes through Store certification the way an MSIX does.

**Acceptance criteria (when implemented):**
- [x] SignPath Foundation eligibility confirmed by actually applying (not just reading their public
      criteria) — **rejected 2026-07-23** (T-F124). Real outcome, not a stale "not yet applied"
      state — Phase 1 below assumed this would be approved and needs re-deciding against the
      fallback options in the cert-options table above before any of the checkboxes below proceed.
- [ ] Phase 1: `Identity Publisher` in `Package.appxmanifest` updated to match the SignPath-issued
      certificate's Subject; `Deploy.ps1`'s signing step re-pointed from local `SignTool` + dev
      cert to SignPath's CI-integrated signing
- [ ] Phase 1: all `.exe`/`.dll` binaries signed via SignPath, including standalone `pakko.exe`
      (`Archiver.CLI`/T-F09) published via `scripts/Publish-Cli.ps1` — not just binaries inside
      the MSIX
- [ ] Phase 1: MSIX signed via SignPath — installs without SmartScreen warning for direct/GitHub
      downloads (current distribution model)
- [ ] Timestamp applied to every signature
- [ ] Signing wired into the actual release process, not a manual one-off step, for both
      `scripts/Publish-Cli.ps1` (CLI) and `Deploy.ps1` (MSIX)
- [ ] Certificate/signing credentials not in the repository
- [ ] `Get-AuthenticodeSignature` returns `Valid` on all binaries, including `pakko.exe`
- [ ] Phase 2 (deferred, tracked here for when it becomes relevant): when the MSIX is actually
      submitted to the Store, `Identity Publisher` updated again to the Partner-Center-assigned
      Publisher ID before that specific submission, and SignPath signing dropped for the MSIX
      going forward (kept for `pakko.exe` regardless)

---

### T-F91 — Multi-Language Localization (OS-Language Auto-Match, English Fallback)
- [~] **Status:** (2026-10-03) all 37 locales ship in every localized frontend (the CLI stays English by design, T-F209): the App's `.resw`, Shell's `OperationText.resx`, the Explorer menu table and `Archiver.Messages` (App and Messages key parity are test-enforced). Left for the release: a native-speaker review and an on-screen layout check per language, RTL (he/ar/ur) included, plus T-F268 (g)'s caption-button tooltips. Earlier status: partial — first batch (all 24 European locales) implemented 2026-07-07;
      Arabic/Japanese/Chinese/etc. (the non-European half of the target list) not started;
      on-device verification, layout-corruption check, and native-speaker translation review
      still outstanding for the European batch. See `DECISIONS.md`'s T-F91 entry.
      **Parity gap found and fixed 2026-07-14:** every string key added by later features
      (T-F05's browse-mode columns/buttons/tray menu/archive-option items, T-F06's conflict
      dialog) had only ever been added to `en-US` and `uk-UA` — the other 22 European locales
      were silently 37 keys behind (31/68 real keys), falling back to English for a large
      fraction of the UI. Found by diffing `<data name=` counts across all 25 `Resources.resw`
      files rather than trusting this doc. Translated the missing 37 keys into all 22 locales
      (bg/cs/da/de/el/es/et/fi/fr/hr/hu/it/lt/lv/nb/nl/pl/pt/ro/sk/sl/sr-Latn/sv), matching each
      locale's existing established terminology; all 25 locale files now carry the same 68 real
      keys (`en-US` stays at 70 — its 2 non-translatable URL keys are deliberately absent from
      every other locale, per this task's own design). `dotnet build src/Archiver.App.csproj`
      confirmed 0 errors with the expanded resources.
      **Non-European batch AI-translated 2026-07-15** (user-directed — see below on why this is
      AI translation, not native-speaker review): all 12 previously-unstarted locales now have a
      full `Resources.resw` with the same 68 real keys — `ar-SA` (Arabic), `ja-JP` (Japanese),
      `zh-Hans` (Chinese, Simplified — no region/dialect specified by the user, chose the
      Simplified/mainland default per Windows' own MUI convention), `id-ID` (Indonesian), `hi-IN`
      (Hindi), `vi-VN` (Vietnamese), `tr-TR` (Turkish), `ko-KR` (Korean), `ur-PK` (Urdu), `th-TH`
      (Thai), `he-IL` (Hebrew), `sw-KE` (Swahili). `dotnet build src/Archiver.App/Archiver.App.csproj
      /p:Platform=x64` confirmed 0 errors/warnings and the generated `AppxManifest.xml` lists all
      37 `<Resource Language>` entries (was 25) with no manual manifest edit — `x-generate` picked
      up all 12 new folders automatically, as designed. **Known limitation, not fixed by this
      batch:** Arabic/Urdu/Hebrew text is translated into the correct RTL script and will shape
      correctly character-by-character (Windows' own text renderer handles that), but Pakko's XAML
      never sets `FlowDirection` — the overall UI layout (button order, alignment) stays
      left-to-right rather than mirroring, which is its own separate scope this task's acceptance
      criteria never asked for (only "layout corruption" — clipping/truncation — was in scope, not
      full RTL mirroring). Worth a follow-up task if true RTL mirroring is ever wanted.
      Native-speaker review pass for all 36 non-English locales remains outstanding — text is
      AI-translated to a professional-UI standard throughout, consistent with this task's own
      "don't ship an unreviewed MT dump" caution; see the criterion below for why this batch could
      only close the missing-language gap, not the review requirement itself.
- **Priority:** low ("nice to have" bonus, per user)
- **Depends on:** none

**What:** `src/Archiver.App/Strings/` currently has only `en-US/Resources.resw` — the app is
English-only. WinUI 3 + MSIX already auto-select the UI language from the OS display language
via resource qualifiers (folder name = BCP-47 locale, declared in `Package.appxmanifest`'s
`<Resources>` element) — no app code is needed for the matching itself, only the translated
`Resources.resw` per locale plus the manifest declarations.

**Explicitly out of scope (confirmed with user):**
- No installer-time language picker — MSIX has no install-time UI to add one to.
- No install-location picker — MSIX always installs to the sandboxed `WindowsApps` path;
  there is no user-choosable install directory on this platform. Document as a non-goal in
  `DECISIONS.md` rather than revisiting.
- No in-app manual language override — OS-language auto-match only, per user's stated scope.

**Target language list (confirm before starting translation work — large scope, deliver
incrementally per locale rather than all at once):**
- European, human-quality translation, **excluding Russian and Belarusian**: Ukrainian, German,
  French, Spanish, Italian, Polish, Portuguese, Dutch, Romanian, Czech, Slovak, Hungarian, Greek,
  Swedish, Danish, Finnish, Norwegian, Bulgarian, Croatian, Serbian, Slovenian, Estonian, Latvian,
  Lithuanian
- Additional (user-requested, beyond Europe): Arabic, Japanese, Chinese, Indonesian, Hindi,
  Vietnamese, Turkish, Korean, Urdu, Thai, Hebrew, Swahili
- **Explicitly excluded:** Persian/Farsi (per user — Iran)
- Any OS language not on this list falls back to `en-US` — WinUI 3's `ResourceManager` does this
  automatically as long as `en-US` stays the manifest's neutral/default language.

**Note on translation quality:** user asked for "human" quality, not raw machine translation —
each locale needs a native-speaker pass or at minimum a correctness review before shipping;
don't ship an unreviewed MT dump under a locale folder.

**Acceptance criteria:**
- [x] Final language list confirmed with user before translation work begins — European batch
      (all 24 locales) confirmed 2026-07-07; non-European batch (Arabic, Japanese, Chinese,
      Indonesian, Hindi, Vietnamese, Turkish, Korean, Urdu, Thai, Hebrew, Swahili) confirmed
      2026-07-15
- [x] `Resources.resw` created under `Strings/<locale>/` for each confirmed locale, translating
      every key already in `en-US/Resources.resw` — all 36 non-English locales (24 European +
      12 non-European) now carry the same 68 real keys (2 URL keys deliberately omitted
      everywhere except `en-US`, see `DECISIONS.md`)
- [x] `Package.appxmanifest`'s `<Resources>` element declares every shipped locale — confirmed
      automatic via the existing `<Resource Language="x-generate"/>`; generated `AppxManifest.xml`
      lists all 37 locales after a `dotnet build`, no manual manifest edit needed (was 25 before
      this round's 12 non-European additions)
- [x] OS display language automatically selects the matching `Resources.resw` with no app code
      change — verified on-device for `uk-UA` 2026-07-15 via a direct screenshot of the installed,
      packaged app (Windows UI-automation MCP wasn't loaded this session — used a self-contained
      PowerShell `GetWindowRect`/`CopyFromScreen` capture instead, see `.claude.local.md`). This
      pass is also what found and fixed **T-F104** — the Archive/Extract buttons never actually
      respected any locale (dead resw keys), and the empty-state hint text was clipped — both real
      bugs unrelated to this criterion itself but caught while verifying it
- [ ] An excluded/unsupported OS language (e.g. `ru-RU`) falls back to `en-US` text, not a
      blank string or resource-load crash — **not verified this round.** The only way to exercise
      this without an in-app language override (a confirmed non-goal above) is to reorder the
      Windows display-language list, which is a system-settings change outside what an agent should
      do unattended — needs either the user doing it themselves, or the Windows MCP automation tool
      (not loaded this session) if it has its own mechanism
- [x] No installer-time language picker or install-location picker added (confirmed non-goal)
- [~] Max text-length budget determined per UI string (buttons, labels, dialog titles) — not
      systematically done across all 36 locales, but the one real overflow this round's on-device
      pass surfaced (the empty-state hint text, locale-independent — see T-F104) was found and
      fixed. No locale-specific "this translation is too long for its control" case confirmed yet
- [~] Manual on-device check for layout corruption (clipped/overlapping/truncated text, buttons
      that no longer fit their label) on at least one long-text locale (e.g. German) and one
      wide-glyph/RTL locale (e.g. Arabic or Hebrew) — done for `uk-UA` only (found and fixed a real
      clipping bug, T-F104); German/Arabic/Hebrew specifically still need either the user's own
      on-device pass with the display language changed, or the Windows MCP tool
- [x] `dotnet build src/Archiver.App` succeeds with all new resources — 0 warnings, 0 errors
- [x] `DECISIONS.md` entry: MSIX install-location non-goal + language auto-match mechanism
- [ ] Native-speaker/correctness review pass on all 36 non-English translations before shipping —
      current text (including the 12 non-European locales added 2026-07-15) is AI-translated to a
      professional-UI standard but unreviewed, per this task's own "don't ship an unreviewed MT
      dump" requirement. An AI agent cannot substitute for this — genuinely needs a human native
      speaker per locale

---

### T-F132 — Sandbox the ZIP Path Like tar.exe (speculative, not scheduled)
- [ ] **Status:** future, deferred indefinitely — recorded so the idea isn't lost, not because it's
      planned work. Added 2026-07-24 after a hypothetical raised during a Reddit-thread security
      discussion (see `SECURITY.md`'s "Native decompression 0-day in the ZIP path" row in Known
      Limitations, added the same session). **Do not pick this up without a real trigger** — see
      Acceptance criteria below for what that trigger looks like.
- **Depends on:** none

**The question:** could `ZipArchiveService`'s ZIP handling (`System.IO.Compression`) be sandboxed
the same way `TarSandboxedService` sandboxes tar.exe (T-F52)?

**Answer: technically yes, but not justified today.** The sandbox subsystem
(`AppContainerProfile`/`QuarantineAcl`/`SandboxJobObject`/`SandboxedProcessLauncher`) is already
generic, not tar.exe-specific — but AppContainer confines a *process*, not a code region within
one. ZIP currently runs as an in-process library call inside `Archiver.App`/`Archiver.Shell`
itself; sandboxing it the same way would require carving it out into a new standalone worker
executable (a real new build/sign/ship artifact) launched via the existing launcher, not a config
flag on the current code.

**Why not now:**
- **No confirmed exploit to close.** T-F52's tar.exe sandbox was justified by a *demonstrated*
  exploit (T-F49's symlink escape) — this would be preemptive hardening against a theoretical
  native-decompression memory-corruption bug with no track record against this specific code path.
  `System.IO.Compression` is one of the most widely used, most audited parts of the entire .NET
  BCL — arguably broader real-world scrutiny than libarchive ever had.
- **Real performance cost, not hypothetical.** T-F35's parallel ZIP pipeline gets its speed from
  multiple threads inside one process; T-F114 already measured process-spawn overhead as the
  dominant cost in the many-small-files scenario for an *external* tool (7za.exe) — the same
  overhead would apply here, on every operation (or every batch, if redesigned to spawn once per
  batch instead of per file — a real design question, not solved by this entry).
- **New artifact, new attack surface of its own** (a new signed exe, new IPC protocol for
  progress/results across the process boundary) — not free to add.

**What would actually trigger picking this up:**
- [ ] A real CVE lands against `System.IO.Compression`'s Deflate path (or the native zlib-derived
      component it calls into) that plausibly affects Pakko's usage — not a CVE in an unrelated
      part of the BCL.
- [ ] Or: a concrete threat-model change for the target audience makes the current "accepted risk"
      framing in `SECURITY.md` no longer acceptable to them.

**If it's ever picked up, first design questions to answer (not answered here):** spawn a fresh
sandboxed worker per archive operation, or per batch (perf tradeoff above)? Reuse
`Archiver.Shell.exe`/`pakko.exe` as the worker, or a new dedicated executable? What IPC shape
carries `ProgressReport`/`ArchiveResult` back across the process boundary without reintroducing the
same "opening every file just to check" cost class T-F86 already rejected for a different reason?

**Empirical spike run 2026-07-24 — real numbers, still doesn't flip the decision above.** Built
`tools/ZipSandboxSpike/` (a committed, not throwaway, worker calling `ZipArchiveService` directly
inside the real sandbox primitives — new profile `Pakko.ZipSandboxSpike`, independent of
production `Pakko.TarSandbox`) plus `tests/Archiver.Core.PerformanceTests/ZipSandboxSpikePerformanceTests.cs`
to actually measure the "real performance cost" bullet above instead of leaving it asserted.
See `DECISIONS.md`'s new T-F132 entry for the full results table and machine spec. Headline: pure
launch/AppContainer/Job-Object overhead is a real, consistent **~90 ms fixed cost per operation**
(not per file — confirms the "amortizes" framing). But relative impact turned out to hinge on
operation length, not file count: negligible (3.6%) for a 10 s archive, dominant (37–77%) for
sub-second operations — and a second, previously-unconsidered cost (worker-process JIT cold-start,
which a discarded warmup *launch* cannot remove since each launch is a fresh process) contributed
more to the delta than the sandbox primitives themselves in 3 of 4 scenarios. Still no confirmed
CVE, so the "no confirmed exploit to close" reasoning above is unchanged and this stays deferred —
but if ever revisited, `PublishReadyToRun`/a persistent pre-warmed worker is now a known real
question, not just AppContainer/Job-Object setup cost (which this spike shows is already cheap).

### T-F144 — Community: r/dotnet post about Pakko (post-Store-release)

- [ ] **Status:** not started — community/marketing task, not a code change. Tracked here (not a
      GitHub Issue) per this project's existing convention of using this file as the single
      backlog, engineering or not.
- **Context:** an earlier r/foss post ("I built a Windows archiver with zero 7-Zip/WinRAR code in
  it, because I stopped trusting their supply chain for gov work") got real engagement — both
  supportive and skeptical (solo-dev trust, AI-generated-code concerns, "why reinvent the wheel").
  Pakko now has a live, certified Microsoft Store release
  (https://apps.microsoft.com/detail/9p5mw010d8pr), which directly answers the "why trust your
  .exe" objection from that thread — worth a follow-up post in r/dotnet specifically.
- **r/dotnet's actual self-promotion rules (confirmed via the subreddit's own rules panel,
  screenshotted 2026-08-06 — not assumed from a third-party summary):**
  - Self-promo posts only allowed on **weekends**.
  - Must be flaired **"Promotion"**.
  - **Must not be written or generated by AI** — "Put some effort into it." (Rule 2 separately
    reinforces this for the whole subreddit, not just self-promo posts.) This means the agent
    should not draft the final post text — only help structure talking points; the user writes
    the actual post in their own words.
  - Restricted to **major/minor release versions** (not patch releases) — current version is
    `v1.4.7`, which qualifies.
- **Planned content (agreed in conversation, 2026-08-06):**
  - Title: technical/informational framing (e.g. "I built an open-source Windows archiver with
    WinUI 3 and .NET 8 — here's the source"), not a bare "check out my app" pitch.
  - Link order: GitHub repo first (https://github.com/pakkoapp-oss/pakko), Microsoft Store link
    second, as a convenience for people who don't want to build from source.
  - Central technical topic: the sandbox-escape exploit found during T-F49/T-F52 design review —
    a naive "extract-to-quarantine-then-validate" model was vulnerable to a symlink entry writing
    outside the quarantine directory before any validation code ran; fixed by pre-scanning and
    rejecting the whole archive before extraction ever starts, plus running extraction itself
    inside an AppContainer sandbox. This is a concrete, verifiable architecture story, not a
    generic feature list.
  - Secondary talking point (if there's room): the zero-byte-file `DeflateStream`/Zip64
    field-offset bugs from T-F35 — both were invisible to Pakko's own test suite and only caught
    by cross-validating output against an independent reader (real 7-Zip), a methodology point
    likely to land well with an engineering audience.
  - Deliberately avoid the "Russian developers" framing used in the r/foss post — it drew sharp
    pushback there even from a sympathetic audience, and r/dotnet is broader/less primed for it.
    Keep the trust argument on supply-chain/reproducible-builds grounds only.
- **Acceptance criteria:**
  - [ ] User picks and confirms a target Saturday (or other weekend day) to post.
  - [ ] User writes the actual post text themselves (per the no-AI rule above); agent may review
        structure/technical accuracy on request, not author it.
  - [ ] Post published with the "Promotion" flair.
  - [ ] Result (received well / removed / notable feedback) recorded back into this entry after
        the fact, since it may inform whether a similar r/Windows post is worth attempting later.
- **Reported by:** user, 2026-08-06 — "Створи під це таску... таску не забудь під публікацію на
  дотнеті сабредіті для мене" (open a task for this — don't forget the task for the r/dotnet
  subreddit post).

---

### T-F152 — VirusTotal Hash Lookup Link in Archive Browser (deferred — rejected as-scoped)

- [~] **Status:** deferred 2026-08-10, user-directed — **not implemented, kept as a real backlog
  entry rather than silently dropped.**
- **Context:** user proposed replacing (or adding alongside) the Archive Browser's per-entry CRC32
  column with a SHA256/MD5 value, shown as a clickable link that opens
  `virustotal.com/gui/file/<hash>` in the user's default browser — hash-only lookup, no file
  upload, nothing sent from Pakko's own code (`Launcher.LaunchUriAsync`, the same mechanism
  already used for the About dialog's GitHub/Ko-fi/Privacy links). Later refined during scoping:
  keep CRC32 as-is (it's free — already read from the ZIP header, zero extra compute), and only
  compute SHA256/MD5 on an explicit double-click (opens the link), with a single click copying the
  hash to the clipboard instead — so the cost only exists at the moment a user asks for it, not on
  every browse.
- **Why deferred, not designed further this round:** the real blocker isn't the UI or the compute
  cost — both are solvable (the double-click/single-click split, and SHA256 needing a real
  recompute since it isn't already available like CRC32 is, especially costly for tar-family
  entries which would need the same quarantine/sandbox path a real Extract uses). The blocker is
  that Pakko's published Privacy Policy (`docs/privacy.html`), `SECURITY.md`, and `README.md` all
  currently make an unqualified claim: "Pakko does not make any network requests... does not
  integrate with any third-party services." A VirusTotal link is not a network call *from Pakko's
  own code* — it opens the user's own browser, the user's own click, the user's own hash — but
  Pakko's specific audience (Ukrainian government/defense, per `CLAUDE.md`'s stated positioning)
  chose this app partly *because of* that unqualified "zero network" claim, and a third-party-
  service link risks reading as a quiet walk-back of that promise even though it's technically
  accurate that the app itself stays offline. Asked the user whether to draft an explicit, honest
  carve-out for the Privacy Policy/SECURITY.md/README ("Pakko does not make network requests,
  except when you explicitly click a VirusTotal link, which opens your own browser") before
  writing any UI code — **user's answer: leave the policy text as-is; don't implement this
  feature.** Recorded here as the actual decision, not as an open question, so this isn't
  re-litigated from scratch in a future session without this context.
- **If ever revisited:** this is not a "come back once you've done more design work" deferral —
  it's a "the user weighed the tradeoff and chose the current unqualified policy text over this
  feature" deferral. Revisiting it means asking the user again whether that tradeoff has changed
  (e.g. if a herметичний, hash-only VirusTotal *file-scan* extension of the existing sandboxed
  AMSI scanning is what's wanted instead — see the CI-side "upload release artifacts to VirusTotal"
  idea floated in the same conversation, which is a different, also-deferred idea about signing
  releases, not this per-entry Archive Browser feature).
- **Reported by:** user, 2026-08-10 — floated the idea, then explicitly declined once the Privacy
  Policy conflict was surfaced: "ні нехай буде як є просто додай беклог як і рішення по цьому."
- **Depends on:** none

---

### Fix batch (next, after the current batch closes) — index

User decision 2026-09-24: the current batch is discovery (T-F202, T-F226);
**every fix below is a separate, next batch** (T-F197 was the one planned exception — folded in
here with T-F227/T-F228 by user decision 2026-09-25). This index is the batch's scope and order; the
task entries themselves hold the detail. Rules for every item: tests first (write the reproducing
test, confirm it fails, then fix — `CLAUDE.md`'s revert-and-confirm rule), the four test
categories, deploy and verify on device before marking done.

**0. Decisions — taken 2026-09-25** (the user delegated them: "your choice, based on our niche
and best practice for similar apps like NanaZip"). Criteria: the government/defense audience
(trust, auditability, no silent data loss), then 7-Zip/NanaZip parity where users carry habits.
- T-F233 — **P0.** Permanent, silent, affects other people's access on shared folders. No
  automatic remediation (rewriting users' ACLs is itself risky): a release note/security advisory
  with a read-only detection script (files carrying a Pakko sandbox SID ACE) and a manual fix that
  removes **only** those ACEs and re-propagates inheritance (not `icacls /reset`, which would also
  wipe explicit ACEs the user set on purpose), with `icacls /save` first. Validate both on the one
  real affected file in Downloads. The advisory touches `SECURITY.md` — separate explicit user
  permission. Fix: never touch the original's security descriptor (stage by copy).
- T-F234 — **P0.** Silent loss of files inside one archive. Fix direction: 7-Zip's actual rule
  (read in NanaZip's vendored `Archive/Zip/ZipItem.cpp:405-461` and `ZipItem.h:338-351`): bit 11
  -> UTF-8; else a valid Info-ZIP Unicode Path extra field (0x7075); else by the central header's
  host OS — Unix -> UTF-8, FAT/NTFS -> OEM code page, other -> ANSI code page. Plus a collision
  check so two entries never collapse into one without an error. One decoder shared by extract,
  list/browse, test and scan. Tests use explicit code pages (866/437/1252), never the machine's;
  `CodePagesEncodingProvider` is registered once at a documented startup point, not in a
  service's static state.
- T-F201 — **keep T-F88's multi-instance design** (7-Zip File Manager and NanaZip open one window
  per archive too); fix only the stacking: offset the new window and bring it to the foreground.
- T-F205 — **keep the archive's root folder** in SingleFolder mode ("extract with full paths",
  7-Zip/NanaZip "Extract here" and `7z x`). Update `docs/DECISIONS.md` (T-F156's note) and the
  tests that encode the strip.
- T-F206 — **`pakko x` without `-o` extracts into the current directory**, like `7z x` (the CLI
  is 7z-familiar by design). User-visible change: CHANGELOG + `--help` + `docs/CLI.md`.
- T-F210 — **keep T-F107's "Up" navigation, add an explicit way back** to create mode (a "Close
  archive" command, also on Esc), as 7-Zip/NanaZip's file manager lets you leave an archive
  without closing the window.
- T-F241 — **add "Test archive" to the App** (7-Zip/NanaZip file managers have it on the
  toolbar); **no threat scan in the CLI for now** — `7z` has no such command. Stated limitation:
  `MpCmdRun` scans ordinary archives but cannot see inside password-protected ZIPs (T-F194's
  point), so a scripted scan of those needs the App or Explorer; record this in `docs/CLI.md`/
  `docs/DECISIONS.md`, revisit on request.
- T-F207 — deletion after an operation goes to the **Recycle Bin**, and only after the T-F260
  classifier says the source was fully processed. Where the OS cannot recycle (network or
  removable drive, item too large for the bin) an explicit "this deletes permanently" confirmation
  naming the items is shown first; declining keeps the source.
- T-F199 — direction: options before the action, primary action last, password inline; the
  mockup is still shown to the user before any XAML changes (visual design is not delegable blind).
- T-F260 — **reverse DECISIONS T-F68/T-F87**: `ArchiveResult` gets an explicit outcome and a
  per-source result. **Cancellation rule: both engines throw `OperationCanceledException`** after
  cleanup (ZIP already does; tar changes) — the .NET idiom, and every frontend already catches it;
  documented in `IArchiveService`/`ITarService` as the one exception to "never throws".
- T-F261 — **gate listing by Group Policy** (T-F250 as filed): an administrator's "never spawns
  tar.exe" must hold; correct `docs/ARCHITECTURE.md:1485`.
- T-F263 — **per-process preview/nested cache roots** plus a startup sweep of folders owned by
  dead processes (the App is multi-process by design, T-F88).

**Roots (architecture review 2026-09-25):** T-F260 (leaves T-F229, T-F245, T-F242, T-F207),
T-F261 (T-F250, T-F216, T-F241, T-F262), T-F263 (T-F227, T-F228, T-F197, T-F248, T-F252, T-F244;
related T-F233), T-F264 (T-F213, T-F159). Only the slice of a root its early leaves need goes
with them (T-F263: a unique staging name + the shared committer for the P0 trio; T-F260: the
outcome + the "may this source be deleted" classifier); the rest is placed by the phase plan. Grouping
roots with no number: Core message codes (T-F209, T-F208, T-F215, T-F221, T-F254), one directory
walker (T-F236, T-F237, T-F251), boundary encoding (T-F204, T-F234, T-F238).

**Phase 4b (added 2026-09-26, user; in progress, after the v1.5.0 release):** T-F268 (Explorer
windows through one `IOperationUi`, then a modern window — step 1 done, step 2 spike next),
T-F269 (Cancel stops the whole multi-archive selection — decide with T-F268 step 3), T-F216
(first half done via T-F268). Runs before phase 5.

**Phase 4c (added 2026-09-26, user):** T-F271 (many-small-files ZIP archiving ~50% slower on
.NET 10 — investigate, then fix or record), T-F272 (VeryLarge `ExtractAsync_OneLargeFile` fails in
its own suite run, pre-existing on .NET 8 too). After T-F270 lands; runs before phase 5.

**Plan update (2026-09-28, user requests; grouped by shared code and shared check, one commit
and one test-first cycle per task):** G0 CI/Sonar for the last pushes. G1 App: T-F276 first
(wording in the operation window, Core texts and CLI stderr — CHANGELOG line), T-F278, T-F277,
T-F219, T-F198 item 5, new keys in 37 locales; one Deploy and one full App pass that also closes
wave 4 (T-F199), the stale statuses (T-F210, T-F212, T-F224, T-F267), T-F213's check and a re-check
of T-F268 (f) with the display confirmed on. G2 tar/sandbox: T-F273 (first, P1), T-F214, T-F171
(short design approved by the user first), T-F148 sandbox/tar only, then T-F287 (every other
P/Invoke, added 2026-09-29 by the user — `SHFileOperationW` guards the Recycle Bin); one
integration + Slow run and one packaged-identity device check. G3 only if T-F268 (f) reproduces. G4 on main between groups:
T-F240, T-F259, T-F203 (after G2). T-F289 (ZIP extraction keeps entry dates; now T-F298) goes after v1.6.0 (user, 2026-09-29). G5 docs: T-F257 (SECURITY.md needs permission), T-F258, T-F223,
T-F165. G6 one device campaign on CI artifacts: T-F202 (coverage table rebuilt from current
source), every `[~]` check (delegated), T-F221 terminal, T-F260 mapping, light theme, keyboard,
narrow window; Windows display language last (sign-out ends the agent session; restore after).
G7 release v1.6.0 (watch the tag run's `release` job: its `download-artifact@v8` `pattern`/`merge-multiple` step runs only on a tag, since Dependabot #4-#8, 2026-09-29) — **user decision 2026-09-28: T-F202's P0/P1 findings are fixed before the
release, P2/P3 go to the next batch.** User: real drag done (T-F242 closed, T-F278 filed),
permission to switch theme and language given. After the release, separate batches: T-F275,
T-F132, T-F121, T-F111, T-F112, remaining P/Invoke conversion, distribution tasks (T-F119, T-F10,
T-F127, T-F144, T-F152).

**1. P0 — data loss or a broken core flow:** T-F227, T-F228, T-F229, T-F204, T-F233,
T-F234 (both P0, decision 2026-09-25), T-F245 (with T-F229), T-F246 — both P0 by user decision 2026-09-25. Suggested order: T-F227 + T-F228 + T-F197 together (same staging/commit code), then
T-F229 with T-F207, then T-F233, T-F234, T-F204.

**2. P1 — broken or misleading feature:** T-F230, T-F231, T-F232, T-F235, T-F236, T-F237,
T-F200, T-F205, T-F206, T-F207, T-F208, T-F210, T-F211, T-F212, T-F213, T-F224, T-F225, T-F247,
T-F248 (with T-F233), T-F250 (P1 kept, user decision 2026-09-25), T-F251 (with T-F236), T-F260,
T-F261, T-F263 (roots — with their leaves).

**3. P2 — polish, consistency, hardening, debt:** T-F198, T-F199, T-F201, T-F203 (SonarCloud),
T-F209, T-F214, T-F215, T-F216, T-F217, T-F218, T-F219, T-F220, T-F221, T-F222, T-F223 (+ T-F165,
diagrams — redo the per-arrow ground-truth ritual T-F226 deferred, after the P0 fixes land),
T-F238, T-F239, T-F240, T-F241, T-F242, T-F243, T-F244, T-F249, T-F252, T-F253, T-F254, T-F255,
T-F256, T-F257, T-F258 (with T-F223/T-F165), T-F259, T-F262, T-F264.

**Not in this batch:** T-F202's user-only checks (light theme, en-US, keyboard-only, tray, CLI in a
real console) and T-F226's deferred per-arrow diagram ritual — carried as open items on those tasks.

### T-F202 — Full UI smoke test: every feature, every menu and submenu (batch gate)

- [~] **Progress 2026-09-24 (discovery pass run against CI build 1.4.12.9):** covered — Explorer
  menu in all 9 selection contexts plus the classic menu, every Explorer command via
  `Archiver.Shell.exe` (incl. conflict, password, bomb dialogs), both App modes control by control,
  `.7z` cold/warm activation, every CLI command and most switches, and heavy scenarios on
  multi-GB real data (3.4 GB ZIP test/extract, 5.1 GB ZIP and TAR create + round trip, all
  byte-identical to the source). Findings filed as T-F204-T-F225 (plus additions to T-F197,
  T-F198, T-F199, T-F201). **Still open:** light theme and en-US (need a system setting change),
  keyboard-only pass (focus not reliably visible to automation), tray icon, the CLI in a real
  console (masked `-p`, Y/N/A/S/U/Q, Ctrl+C, PowerShell 5.1). Caveat: the test machine's ANSI code
  page is 65001, so code-page bugs 1251/1252 users would hit are invisible here. (Correction
  2026-09-25: `GetACP()`/registry now report ACP 1251, OEMCP 866 — see T-F204.) Coverage table,
  results and the N-number-to-task mapping: batch plan section 5.
- [~] **Progress 2026-10-29 (v1.9.0, package 1.8.1.22, a real `conhost` console driven through the `windows`
  MCP server, the screen buffer read back with `ReadConsoleOutputCharacter`):** `pakko x enc.zip -p` prompts
  "Password for enc.zip:", echoes `*` per key, extracts with the typed password (`CLI-013`); the overwrite
  prompt shows both sizes and times, asks again after an unknown answer, `u` renames both (`a (1).txt`),
  `q` stops with "operation stopped by user", exit 255; Ctrl+C at the prompt exits 255 and writes
  nothing. Windows PowerShell 5.1 is `CLI-009`; the keyboard-only pass is `APP-012`.
- [ ] **Status:** open — a required gate for closing this batch (user instruction 2026-09-24: the
  app is live on the Microsoft Store and earlier self-testing missed real defects). Agent-driven via
  `windows` MCP against the freshly deployed MSIX, with a written checklist and a pass/fail per item:
  every button and option of the main window in both modes; every Explorer context-menu command
  and submenu (Pakko root, Extract Here/to folder/Open, Add to X.zip/.tar, Test, Scan for threats,
  Hash) on files, folders, multi-selection and a drive root; every dialog (conflict,
  password decrypt/encrypt, summary, bomb warning, About); the CLI commands including the real-
  console prompts; ZIP and tar-family formats; Ukrainian and English UI. **Discovery only (user
  decision 2026-09-24):** every defect found, however small, becomes its own task with repro steps
  and priority; fixes go to a separate follow-up batch (with T-F198-T-F201), tests first. **Method (user instruction 2026-09-24):** before the first click,
  build a coverage table — one row per action or menu item, including every Explorer context-menu
  dropdown submenu and the classic "Show more options" menu: surface, menu path, selection context,
  expected behavior, visual check (icon, text, locale, order, separators, enabled state,
  light/dark theme), result, UX notes. The tester acts as QA + UI/UX reviewer: judge the app's
  overall behavior while testing (feedback, consistency, dangerous actions, stray windows, focus,
  accessibility), not just pass/fail. **CLI:** a smoke test of every command/switch plus a deep
  usability review of `pakko.exe` against real `7z` and CLI best practice (ripgrep/fd/bat/gh/tar/
  curl: help and error text, switch consistency, TTY/color/`NO_COLOR`, progress, quiet/verbose,
  scripting stability, exit codes, Unicode/long paths); every divergence from 7z is either
  documented as deliberate in `docs/CLI.md` or filed as a defect. Also carries the diagram gap from DECISIONS' T-F193 entry (no
  diagram models `ArchiveAsync` routing; diagram 3 has no encrypted-entry branch).
- **Reported by:** user instruction, 2026-09-24.

### T-F202 findings (T-F204 onward) — discovery pass 2026-09-24

All found against the CI build of commit 8952c12 (MSIX 1.4.12.9 x64, CI run 36020977383, and that
run's `pakko.exe` win-x64), agent-driven via the `windows` MCP plus direct `Archiver.Shell.exe`
launches with the exact arguments each `IExplorerCommand::Invoke` builds. Paths are shown relative
(`<scratch>\...`). Priority: **P0** = data loss/corruption or a broken core flow, **P1** = broken
or misleading feature, **P2** = polish/consistency. Fixes belong to the follow-up fix batch, tests
first (user decision 2026-09-24). Items marked **decision** reverse or touch an earlier documented
choice — ask the user before implementing, like T-F118/T-F156 were.

### T-F254 — Explorer menu stays English for Chinese and regional-variant Windows languages (P2)

- **Deferred past 1.9.0 (2026-10-24):** needs a regional display language pack and a sign-out.
- [~] **Status:** partial — fixed in code (see Progress). App device-checked 2026-10-04 (v1.7.0 wave 9, CI build of 039955a): with de-AT first in the user language list (no German display pack), the App window comes up in German ("Dateien hinzufügen", "Komprimieren als ZIP"). Left: the Explorer menu and Shell on a machine whose display language is regional (needs the language pack and a sign-out; user chose not to do it for v1.7.0) — covered by `LocalizationTests`/`UiCulture` tests only.
- [~] **Progress (2026-09-28, fix phase 7):** fixed in 4795bfb (C++ menu) and eb5876e (Shell `.resx`): exact tag (case-insensitive), zh-CN/zh-SG/zh-Hans-* -> zh-Hans, Traditional stays English, else the same language's row. Same rule in `Archiver.Messages.UiCulture`; the App picks the first of `ApplicationLanguages` Pakko translates. Needs a device with a regional language (e.g. de-AT) to confirm.

- **Earlier status:** open — code-confirmed 2026-09-25. `Localization.cpp` looks the UI language up by
  exact tag (`GetLocalizedString`, `:112-121`) from `GetThreadPreferredUILanguages` (`:94-110`).
  Windows reports Simplified Chinese as `zh-CN` (Microsoft's language-pack table, fetched
  2026-09-25), but the table's key is `zh-Hans` — it can never match, so Chinese users always get
  the English menu. Regional variants with their own Windows language pack (`pt-BR`, `es-MX`,
  `fr-CA`) and any user whose tag differs from the table's one region (`de-AT`, `de-CH`, ...) also
  fall to English. The other frontends resolve differently: `Archiver.Shell`'s `.resx` use .NET's
  parent chain (`zh-CN` -> `zh-Hans` works, `de-AT` -> `de` -> invariant does not), the App uses
  MRT language matching (its `de-AT` behavior not verified) — so the menu, Shell dialogs and window
  can disagree, localized in one and English in another.
  `LocalizationTests.cpp:62` feeds only the table's own keys, so it cannot catch this.
  `docs/DECISIONS.md`'s T-F115 entry (`:4816`) already noted `zh-Hans` does not map to a
  classic tag, but not the lookup consequence.
- **Tests first:** `GetLocalizedString(id, L"zh-CN")` returns the Chinese row; `pt-BR`/`de-AT`
  fall back to their language, not en-US.
- **Reported by:** T-F226 batch 3, 2026-09-25.
- **Root (grouping, architecture review 2026-09-25):** Core reports errors and skips as English text with no code (`ArchiveError`/`SkippedFile` hold only strings), and the frontends localize through four separate mechanisms (App `.resw`, Shell `.resx`, `Localization.cpp`, none in the CLI). Fix T-F209/T-F208/T-F215/T-F221/T-F254 together: a code in the Core model, rendered per frontend.

### Architecture review findings (T-F260 onward) — 2026-09-25

One level above T-F226's rule-per-component pass: layer boundaries, duplicated decisions, data and
state flow, extensibility. Done by the main session plus one independent reviewer agent that was
not given the defect list (it reached 7 of the same 10 roots on its own). **Rule for these
entries:** a root gets its own task only when the root fix is work no leaf task covers (a public
Core contract change, or reversing a recorded decision). Each covered leaf carries a
`**Root:**` line; the slice of the root a leaf needs goes with that leaf, the rest is placed
by the phase plan. Roots that are only a grouping
(Core message codes, directory walking, text encoding across process boundaries) have no number
here — see the `**Root:**` notes on T-F209, T-F236/T-F237/T-F251 and T-F204/T-F234/T-F238.

### T-F317 — `pakko` in the terminal for Store and winget users (P2, user-requested)

- **Deferred past 1.9.0 (2026-10-24):** the winget-pkgs PR #446296 was closed unmerged; a new one needs the user's CLA and Microsoft's review.
- **2026-10-31:** in the v1.10.0 wave (stream F, the user). #446296 was closed by us as superseded
  by #449503 (1.7.2), open, validated, CLA signed, waiting for a moderator.
- [~] **Status:** done in v1.7.0 wave 8 (2026-10-03) except the outward steps. The first
  `microsoft/winget-pkgs` PR is open (user's go and identifier `PavloRybchenko.PakkoCLI`,
  2026-10-03; from `pakkoapp-oss`, manifest schema 1.12.0):
  https://github.com/microsoft/winget-pkgs/pull/446296 — the Microsoft CLA is for the user to sign
  through the bot's comment. Left: that PR merged and `winget install pakko-cli` checked from the
  community source, the headless-app check at the next Store
  upload (wave 9), an ARM64 device run of the packaged alias, and both `index.html` install lines
  (they describe the released v1.6.0, so they change with the v1.7.0 release and the merged winget
  PR, not before). **CHANGELOG v1.7.0:** a Store or
  MSIX install now gives the `pakko` command in any terminal; the CLI zip can be installed with
  winget (`winget install pakko-cli`).
  - **Part B evidence** (Deploy 1.6.0.9 x64, 2026-10-03): `where pakko` →
    `%LOCALAPPDATA%\Microsoft\WindowsApps\pakko.exe` from cmd, Windows PowerShell and pwsh 7;
    `pakko -v` → `pakko 0.0.0-dev+ce95850 (package PavloRybchenko.Pakko_1.6.0.9_x64__9hkd8feqeqbr4)`;
    the whole Subprocess layer (68 tests) green against the alias (`PAKKO_CLI_EXE`); by hand: `a`
    zip and `-ttar.gz`, `t`, `l` and `x` of a `.tar.gz`, `x` of a `.7z` (tar through the
    AppContainer sandbox from the package identity), `-si` and `-so` through cmd pipes, Ctrl+C in a
    real Windows Terminal during a 1.5 GB `a` → exit 255, "operation stopped by user", no partial
    archive or temp folder left. Size cost: four files, ~300 KB (shares the App's runtime).
    Found: MSIX write virtualization of a new top-level `%LOCALAPPDATA%`/`%APPDATA%` folder — T-F320.
  - **Part A evidence:** `scripts/New-WingetManifest.ps1` for v1.6.0 → `winget validate` OK →
    `winget install --manifest` → `pakko 1.6.0`, `a`/`t`/`l`/`x` OK → `winget uninstall`. The
    symlink mode failed (the .NET apphost looks for `pakko.dll` next to `WinGet\Links\pakko.exe`:
    "The application to execute does not exist"), so the manifest uses
    `ArchiveBinariesDependOnPath: true` (install folder on PATH; the symlink mode is not used).
    `winget uninstall` removed the folder but left its PATH entry (winget's behavior; documented).
- **Earlier status:** open. Requested by the user 2026-10-03; folds in T-F119's winget part. Today a
  Store install gives no `pakko` command, and the CLI zip from GitHub Releases needs a manual `PATH`
  edit (`docs/CLI.md`, Distribution).
- **Part A — winget (CLI zip).** A manifest in `microsoft/winget-pkgs` for the existing per-arch
  zips (`win-x64`, `win-arm64`): `InstallerType: zip`, `NestedInstallerType: portable`,
  `pakko.exe`, `PortableCommandAlias: pakko`, SHA256 from the release's `SHA256SUMS`. Id to be
  checked for availability (e.g. `PakkoApp.Pakko.CLI`); the GUI is already in winget through the
  `msstore` source (9P5MW010D8PR). First version with `wingetcreate new`, validated with
  `winget validate` and a real `winget install --manifest`; later versions from `build.yml` after
  each release (`wingetcreate update` or a pinned action, SHA-pinned per CLAUDE.md). Opening the PR
  to `microsoft/winget-pkgs` publishes under the project's name — only on the user's go. Risk: the
  binaries are not code-signed yet (T-F10); winget accepts that, but its antivirus check may delay a
  submission.
- **Part B — Store package alias.** `pakko.exe` inside the MSIX as its own `<Application>`
  (`EntryPoint="Windows.FullTrustApplication"`) with a `uap3:AppExecutionAlias` `pakko.exe`
  (corrected 2026-10-03: no `desktop4:Subsystem` — NanaZip's real manifest declares its console exe
  without it; the exe's own console PE subsystem attaches it to the terminal), as `Archiver.Shell.exe` is packaged today (`Content
  Include`, self-contained — check first whether it can use the package's own .NET instead of a
  second runtime copy, and measure the size cost). Check before the Store upload that a second hidden
  application does not trip the headless-app check again (T-F129's waiver). Verify inside the
  package identity: `x`/`t`/`l`/`a`, tar through the AppContainer sandbox, `-si`/`-so` staging,
  Ctrl+C, exit codes, from pwsh 7, Windows PowerShell and cmd.
- **Both installed:** a bare `pakko` runs whichever folder comes first in `PATH`
  (`%LOCALAPPDATA%\Microsoft\WindowsApps` vs winget's `Links`). Document it in `docs/CLI.md`; `pakko
  -v` shows which one ran.
- **Naming:** the winget package is named "Pakko CLI" with moniker `pakko-cli`, so `winget install
  pakko` keeps finding only the GUI (msstore) and never asks which one; the command is still `pakko`.
  Unverified until the package is listed: winget's name matching might still offer both — check
  `winget install pakko` once the PR is merged, and soften `docs/CLI.md` if it asks.
- **Docs:** `docs/CLI.md` Distribution (replace "not added to PATH"), `scripts/README.md` (the
  per-release winget step), `README.md` + both `index.html` install lines, DECISIONS entry.
- **Tests first:** a manifest check that the packaged `pakko.exe` has an alias and console subsystem
  (the compiled `AppxManifest.xml`, not the source file), and the CLI Subprocess layer run against the
  packaged exe where it can be.
- **Reported by:** the user, 2026-10-03.

### T-F318 — Reusable deflate compressor for the parallel ZIP writer (P3, future)

- [ ] **Status:** future, waits for the target .NET. Upstream answer to T-F271's report
  (dotnet/runtime#134700, milestone Future, 2026-10-01): .NET 11 adds `DeflateEncoder` with
  `Reset()`, so one compressor per worker can produce independent deflate streams instead of a new
  `DeflateStream` (and new zlib-ng state) per entry. The project is on .NET 10 LTS (T-F270); .NET 11
  is an STS release. Decide when the target moves: then switch `ZipEntryCompressor`'s small-file
  path to a per-worker `DeflateEncoder`, measured with T-F114's `Archive/ManySmallFiles` against
  .NET 8's ~0.86 ratio. Check that an empty entry still writes `Store` (CLAUDE.md, zero-byte rule)
  and that 7za reads the result.
  Our reply (user-approved, from `pakkoapp-oss`, 2026-10-03):
  https://github.com/dotnet/runtime/issues/134700#issuecomment-5970925304 — promises the numbers.
- **Reported by:** upstream reply to dotnet/runtime#134700, 2026-10-01.

### T-F328 — Localization audit fixes: wrong meanings and one term per action in 36 locales (P2)

- [~] **Status:** in progress. A read-through of every translated string (339 keys, 36 locales,
  2026-10-05) found 10 strings with a wrong meaning, 68 with a wrong or inconsistent term and
  ~226 style points. Root cause of most: `CoreMessages` was translated apart from the App and the
  Explorer menu, with no glossary, so one action has two or three words depending on the surface.
  The full per-locale list (key, current text, problem, proposed text) is outside the repo, next
  to the v1.7.0 plan (`pakko-localization-audit-2026-10-05.md`).
- **Phase 1 — wrong meaning (10):**
  - [x] es-ES, el-GR: "one archive / separate archives / new archive" read as "one file / new file"
  - [x] it-IT, sv-SE, hi-IN: `NewArchiveCollapsedReason` says "compressed", not "collapsed"
  - [x] et-EE, lv-LV: menu `Add to "{0}"` lost the "to" (`archiveNamedTemplate`)
  - [x] ar-SA: `AboutTagline` calls the app an archive, not an archiver
  - [x] sw-KE: one word for archive, drive, Save and Keep
  - [x] ur-PK: "inconsistent" translated as "temporary" (`TarListingInconsistent`, `ListingInconsistent`)
- **Phase 2 — one term per action inside each locale (B items):**
  - [x] extract: pl, cs, sk, bg, lv, et, tr; compress: hr, sr-Latn, lt; password: de
  - [x] archive: es, el, ko, vi, ja, zh-Hans, hi, th, sw
  - [x] Test must not read as Scan: uk, lv, el, hi (Core), it, lt (buttons)
  - [x] Clear / Remove from list must not use the word for Delete: hu, vi, sw, id, sk
  - [x] the single-string B items: fi tray menu and MiB unit, hash naming (pl, ro, lv, hr, sl, fi, vi), de/sv/sl
    "cannot be browsed", sk tar.exe word order, es/it/pt `TarListingInconsistent`, he and bg taglines, he
    and sr-Latn list gender, he format term, hu sandbox, ja punctuation word, id Recycle Bin, ur spelling
- **Phase 4 — the audit's concrete style points (96 values):**
  - [x] the compression-level list and its summary line use one word (fr, es, it, pt, ro, el, ko, hi, lt,
    lv, zh-Hans); the decompression-bomb term matches Core (nb, tr, ko, ar, he); grammar and agreement
    fixes (it, sv, cs, sl, ro, nb, fi, et, he, pl, nl); single words that differed between surfaces
    (pt, da, bg, hr, sr-Latn, hu, id, vi, sw, th, hi, uk, ja)
  - [ ] left out: quote, dash and apostrophe normalisation; one grammatical form on buttons in bg and
    he; el junction word; zh-Hans full-width punctuation; hu "hibák" for "issues". None changes a
    meaning; each touches many strings
- **Phase 3 — dropped:** the audit proposed lower-casing the word in brackets of `extractHereIntelligent`.
  That capital is deliberate: the item copies NanaZip's own text (see T-F115's entry and
  `LocalizationTests.cpp`), so nothing changes there.
- **Tie-break for "one term":** the variant the Explorer menu and the main window already share
  wins; where they differ, the more frequent one. Choices: `docs/DECISIONS.md`'s T-F328 entry.
- **Done (2026-10-05):** 609 values in all 36 locales, all through a script that checks the
  current value before it writes.
- **No native review:** none is available for this many languages (user, 2026-10-05), so the
  translations stand on this audit alone; sw-KE, ur-PK and th-TH are the least certain.
- **Out of scope:** `LocalHeaderMismatch` (T-F323 rewrites it in all locales); any change to the
  English source text (it mirrors `MessageTemplates.cs`, the CLI's output); quote, dash and
  apostrophe normalisation and the other style points (a later pass); the file-type names in
  `Package.appxmanifest`, which are not localized at all (own task when wanted).
- **Acceptance:**
  - [x] every edit applied by a script that asserts the current value first
  - [x] `dotnet test` default filter green; `Archiver.ShellExtension.Tests.exe` green; the
    ShellExtension DLL builds
  - [ ] one on-device look at a non-English locale (menu, main window, one Core error)
    — needs a non-English Windows session; put in the backlog with no date (user, 2026-10-05), not
    a release gate. Look for
    cut-off labels where the text grew: es and el Test/Close archive buttons, radio buttons and
    the menu's "Add to archive…", hu and vi Clear
  - [x] `Archiver.App` builds with the changed `.resw` files
- **Reported by:** localization audit, 2026-10-05.

### T-F329 — Localization process: glossary, one home for shared strings, counts, translator context (P2)

- [~] **Status:** done except the screenshots (Part 5), which wait for the next retake;
  the five "{0} bytes" messages were done 2026-10-06.
  T-F328 fixed about 600 translated values; nearly all came from four gaps around the loading
  mechanisms, not from the mechanisms themselves. The three mechanisms stay as they are (`.resw`
  for WinUI, `.resx` for Shell and `Archiver.Messages`, the compiled table in `Localization.cpp`).
  Decisions: `docs/DECISIONS.md`'s T-F329 entry.
- **Part 1 — glossary with a test:**
  - [x] `tests/Archiver.Messages.Tests/Glossary.tsv`: 10 concepts x 37 locales (extract, compress,
    archive, password, test, scan, hash, skip, entry, folder), each with the key that shows the
    word and the words T-F328 replaced
  - [x] `GlossaryTests` reads every source through `LocalizedSources` (App `.resw`, `CoreMessages`,
    the six Shell `.resx` families, `Localization.cpp`) and fails on a replaced word. Mutation-
    checked on one `.resw`, one `.resx` and the C++ table. Its first run found 8 leftovers in six
    locales (`LocalHeaderMismatch`, which T-F328 had skipped, and cs/sk `InsufficientDiskSpace`); fixed
- **Part 2 — shared strings:**
  - [x] kept the copies; `SharedStringTests` fails when one English string has two translations
    in one locale. About 40 groups, all already equal. Mutation-checked on all three kinds of source
- **Part 3 — counts before nouns:**
  - [x] English and locales: `OutcomeWillExtract`, `OutcomeWillArchive` (21 locales rewritten; the
    rest had the colon form or do not inflect after a number), the four `Title...Many` titles of
    the operation window (locales already had the colon form)
  - [x] translations only: `TarDuplicateCopiesNotExtracted` (27 locales) and `LocalHeaderMismatch`
    (30 locales) now put the count after the noun phrase
  - [x] the English of those two Core messages, which is `pakko.exe`'s output: changed on the
    user's word (2026-10-05), T-F323 closed
  - [x] `CountTemplateTests` for all English text; a line in `docs/CONVENTIONS.md`
  - [x] the five Core messages with "{0} bytes" (`EntryNameTooLong`,
    `NotEnoughSpaceToCompress`, `InsufficientDiskSpace`, `ZipBombDeclined`, `TarBombDeclined`),
    2026-10-06: in pl, hr, sr-Latn, sl, lt, lv and ro the size is written "{0} B", the unit those
    locales' size columns use (35 values, by script with the old value asserted;
    `CountTemplateTests.ByteCounts_UseTheUnitWhereTheNounInflects` failed for all seven first).
    Why these seven: the noun there changes with the number's last digits (pl "22 bajty", lt
    "21 baitas", ro "20 de octeți"). Left as they are: cs and sk (the genitive plural is right
    from 5 up, and these are sizes), bg (count form), uk ("байт" as a unit), et and fi
    (partitive), ar (the unit word is the usual form), and the locales where only "1" disagrees.
    English is unchanged, so `pakko.exe`'s output is too
  - Not changed: "up to {0} characters" and "longer than {0} characters": `{0}` is always 99, the
    `<comment>` now says so
- **Part 4 — context for whoever translates:**
  - [x] `<comment>` on 42 English strings (App, `OperationText`, `ScanMessages`) and on the two
    menu templates in `Localization.h`: where the string shows and what each `{n}` is
- **Part 5 — what the manifest showed in English only:**
  - [x] `Package.appxmanifest` takes the two file-type names and the app description from
    `ms-resource:` keys (`FileTypeZipName`, `FileTypeArchiveName`, `AppDescription`), translated
    in 37 locales; `AppResourceKeysTests` checks it and counts a manifest reference as a use
  - [x] bundle path (T-F139's failure class): dispatch run 37265510577 on `503eacd` built
    `build-store-msix` (x64, ARM64) and `bundle-store-msix` green; each `.msix` inside the Store
    bundle lists 37 languages, keeps the three `ms-resource:` references and has `resources.pri`
  - [ ] `docs/assets/store/02-main-window-light.png` and its dark and Ukrainian twins show the old
    English footer line ("Will compress 7 item(s) to the folder above"). One line differs, so
    the user decided (2026-10-05) not to retake them now; not a release gate. When they are next
    retaken: since T-F330 the English shots (`01`, `07`, `08` and the main window) need English
    first in the Windows language list, or Pakko's items come out in the listed language
- **Out of scope:** merging the three mechanisms; converting the two escaped fields of
  `Localization.cpp` to literal characters; the style points T-F328 left out.
- **Device check (2026-10-05, dev package 1.6.0.10, agent-driven):** the installed
  `AppxManifest.xml` keeps the three `ms-resource:` references and lists 37 languages; Explorer's
  Type column shows "Архів Pakko" for `.tar` (the display language is English, Ukrainian is first
  in the preferred-language list; the packaged App and the manifest strings follow that list, and
  at the time of this check the Shell helper still followed the display language, fixed since in
  T-F330); Test on three archives, Extract here on two, Compress, Scan on two and Hash on two files
  ran through the installed `Archiver.Shell.exe` with the new titles ("Testing archives: 3",
  "Scanning archives: 2", "SHA-256 (files: 2)") and a Core error line; the main window (fresh
  build time in the title) queued an archive, showed the footer line and extracted it. Not
  checked on device: a non-English display language, or any locale other than Ukrainian
  (T-F328's deferred look).
- **Acceptance:**
  - [x] the glossary test and the shared-string test are in the default `dotnet test` run and green
  - [x] no UI template has a count directly before the thing counted
  - [x] the same for the English Core text ("{0} bytes" stays in English, see Part 3)
  - [x] a file type registered by Pakko shows a localized name in Explorer
- **Reported by:** localization audit follow-up, 2026-10-05.

### T-F331 — Store listing text: errors in the published text, v1.7.0 features, a copy in the repo (P2)

- [~] **Status:** text done 2026-10-05 and imported into Partner Center submission 6 from the
  listing CSV the same day: 36 of 37 languages equal the files, the five missing languages were
  added. Swedish was refused with no reason given; the user imported it later, with the v1.7.1
  submission (the user's word, 2026-10-06; to be compared with the public catalog once that
  submission is live).
- **Found:** the published listing (read from the public Store catalog, equal to the user's
  export of 32 languages) had Arabic and Hebrew stored back to front, a sentence cut in the
  middle in nine languages (cs, da, el, lt, nb, pl, ro, sv, th), words that differ from the app's
  own in thirteen, a stray search term in Spanish, and no mention of anything added since v1.4.
  Five app languages (hr, sl, sr-Latn, ur, vi) have no listing. Full table:
  `docs/store-listing/README.md`.
- **Done:**
  - [x] `docs/store-listing/<locale>.txt` for all 37 locales: description, short description, 12
    features, 7 search terms, "What's new" for v1.7.0
  - [x] `StoreListingTests` (field limits, menu items named as the menu shows them, no word the
    glossary replaced); mutation-checked on all three
  - [x] imported from the exported listing CSV (a script fills the text rows from these files and
    leaves the image rows alone); hr, sl, sr-Latn, ur, vi added as new columns, with the English
    screenshots
  - [x] sv-SE: imported by the user with the v1.7.1 submission (the user's word, 2026-10-06). Before that: refused a second time (third CSV, export of 2026-10-05), the whole language, even
    the copyright line, so the full stop in the search term "tar.gz" was not the cause. The fourth
    CSV gives sv-se only the description, short description, "What's new" and copyright line and
    leaves its features and search terms as published: the next export shows which half is
    refused. Partner Center's own form names the field if the text is entered there by hand
  - [x] screenshot captions (`docs/assets/store/CAPTIONS.md`): not added, see T-F332
- [x] `README.md`, `README.uk.md`, `SECURITY.md` and `docs/SPEC.md` said that Explorer does not
  propagate the Mark-of-the-Web. A test on this machine (Windows 11 build 26300, Explorer's ZIP
  folder) showed that it does for ZIP; corrected with the user's permission, 2026-10-05. To be settled with the feature-text refresh before
  the release commit.
- **Reported by:** user, 2026-10-05.

### T-F332 — Store listing: the optional fields worth filling (P2)

- **Deferred past 1.9.0 (2026-10-24):** the super hero art needs the user's image; every other field is filled.
- [~] **Status:** text done 2026-10-05; the CSV import and the 300 x 300 icon went in with the v1.7.1 submission (the user's word, 2026-10-06). Open: the super hero art, if wanted. Follows
  T-F331; the listing CSV has every field, and these
  are empty in all 37 languages.
- **Text, through the listing CSV** (Partner Center, submission overview, "Export listing" /
  "Import listings"; rows `ShortDescription`,
  `CopyrightTrademarkInformation`):
  - [x] short description at most 270 characters in every language (Microsoft: some views show
    only the first 270): the clause about the right-click menu is gone from all 37 (feature 5 and
    the description have it), the longest is el-GR at 270; the test limit went from 1,000 to 270
    and 29 locales failed it before the text changed
  - [x] copyright line, the same in every language: `Copyright © 2026 Pakko Contributors`, the
    notice in `LICENSE`; kept in `docs/store-listing/README.md`, not in the 37 files
  - [x] the fill script is `scripts/Fill-StoreListing.py` (2026-10-06): the export, the output
    and the listing folder are parameters, `--add-locale` and `--keep-lists` replace the edited
    constants. Run on `listingData-9P5MW010D8PR-filled-4.csv` with `--keep-lists sv-SE` it wrote a
    byte-identical file. Described in `scripts/README.md`
  - [x] the third CSV imported: the next export has the 270-character short description and the
    copyright line in 36 languages (not sv-se, see T-F331)
  - [x] the user imported `listingData-9P5MW010D8PR-filled-4.csv` (the user's word, 2026-10-06): the icon URL copied from uk-ua,
    where it was uploaded, to all 37 languages
- **Images, uploaded by hand:**
  - [x] (uploaded by the user, 2026-10-06 word) 1:1 app tile icon, 300 x 300 ("strongly recommended" for apps; without it the Store uses
    the package's icon): drawn from the rectangles of `src/Archiver.App/Assets/pakko-icon.svg` as
    `docs/assets/store/app-tile-icon-300.png`, 2026-10-05; the user uploads it
  - [ ] 16:9 super hero art, 1920 x 1080: no text, no title, no app UI. Needed to be considered
    for the Store's featured layouts. The user decides whether it is worth drawing
- **Decided against:** screenshot captions (the user, 2026-10-05: the seven screenshots were
  uploaded in one order for every language — 03, 04, 05, 01, 08, 02, 09 of
  `docs/assets/store/CAPTIONS.md` — and Partner Center shows them in a different order from
  language to language, so caption N would sit under a different picture in each; the CSV has
  only URLs, the order cannot be read or set from it); short title, voice title, Xbox images
  (Xbox only); 2:3 poster and 1:1 box art (games); hardware requirements (none beyond the
  package's); "Developed by".
- **The user's call, not started:** sort title ("Pako"), additional license terms (Apache 2.0),
  a trailer.
- **Watch:** a Microsoft Q&A report says a submission with all seven search terms would not
  publish and one with six did (seen only as a search summary, not read).
- **Sources:** Microsoft Learn, "Add and edit Store listing info for MSIX app", "App screenshots,
  images, and trailers for MSIX app", "Import and export store listings for your MSIX app".
- **Reported by:** user, 2026-10-05.

## Performance wave (T-F346–T-F359)

Opened 2026-10-06 from a measured study of start-up and resource use (installed dev 1.7.1.1 x64,
warm starts, 12 cores, Defender on; EventPipe profiles, A/B through environment variables, 7za on
the same data). Baseline: App process start to visible window ~600 ms (CPU ~1000 ms, 138 MB
working set, 60 MB private, 38 threads); Explorer "Open" ~860 ms; Explorer "Extract here" of 200
small files: files written at ~550 ms, Shell exits at ~780 ms. Runtime switches (TieredPGO,
gcConcurrent, gen0size, ConserveMemory) change neither time nor memory and are not tasks. Rule
for every task here: behaviour does not change, only when the work happens; the numbers above are
re-measured with T-F346's script before and after.

### T-F359 — The parallel ZIP writer on a disk with a seek penalty: large files take turns (P2)

- **Deferred past 1.9.0 (2026-10-24):** no spinning disk to measure on; the code is on `main` and acts only where a disk reports a seek penalty.
- [~] **Status:** code done 2026-10-06, **not measured** - the dev machine has no spinning disk.
  When the destination or a source is on a disk that reports a seek penalty
  (`DiskSeekPenalty.IsKnownPresent`), `ParallelSingleArchiveWriter` compresses the files that go
  through a chunk file (over 1 MiB) one at a time (`CompressionSettings.OneLargeFileAtATime`);
  files compressed in memory stay parallel. Applies wherever that writer runs (more than 64
  files, a password, Fastest; both archive modes). The archive's bytes are the same on any disk.
  A share or a disk that does not answer changes nothing - only an explicit "has a penalty" does.
  The disk is asked once per folder the sources sit in, not once per path (T-F352's own
  question still asks per source: it is reached only with 64 files or fewer). Not covered:
  `SeparateArchives` still writes several archives side by side on such a disk, as before. Tests
  first (`ZipArchiveServiceWriterChoiceTests`, `ParallelSingleArchiveWriterTests.OneAtATime_*`,
  `DiskSeekPenaltyTests`); eight mutants, all caught. Stays `[~]` until measured on a spinning
  disk (before/after on a few large files beside many small ones).
- **Reported by:** the user's decision, 2026-10-06.

## After v1.7.2

### T-F424 — `tar.exe` lists a name outside the code page by best fit (`café.txt` as `cafe.txt`) (P3, from T-F418)

- [x] **Status:** done 2026-11-05 with T-F441 (part 2, #132; `CLI-044` on this machine and on
  PakkoW10's 3.5.2). The folder case is T-F443.
- **Before:** in progress under T-F441 part 2 (branch `fix/tf441-tar-verdict`, 2026-10-31):
  `TarNames` judges the listing (header names of a plain or gzip tar, repeated names otherwise, the
  quarantine's files after a full extraction) and refuses a selection or filter it cannot trust.
  Measured on both builds: 3.5.2 lists by best fit too; the silent case is two names collapsing
  into one line. Open: the on-device and VM rows, a folder whose name is a best fit.
- **Before:** open. Measured by T-F418 (bsdtar 3.8.9, cp1251): a tar entry `café.txt` lists
  through `tar -t` as `cafe.txt`, and a filter or a `-T` line naming either matches nothing. T-F416
  catches only the `?` form of a lossy name. Next: tell a best-fit name from a real one (the
  listing's name does not round-trip through the code page back to a stored name), and refuse a
  filter or selection over it with `TarNameNotRepresentable`, as T-F416 does.
- **Reported by:** T-F418's measurement, 2026-10-24.

### T-F427 — The AMSI EICAR tests decide once whether AMSI is live, then fail when it stops (P3, stream B)

- [ ] **Status:** fixed 2026-10-31, merged in #138 (`fix/stream-b-launcher-amsi`), awaiting the device check (stream E): when a scan misses EICAR, both
  tests probe AMSI again as the attribute does; AMSI not detecting now (not ready, or degraded to
  Clean as in T-F177) means not judged, with the reason in the test output (xUnit 2 has no run-time
  skip); AMSI healthy means the strict checks. `ScanAsync_AmsiStopsBeingReadyMidScan_...` pins the
  service's answer with a fake (0x80070015 after the first scan: the threat is Inconclusive, never
  Clean). Both real tests passed on the dev machine with real-time protection on. History:
  `AntivirusScanServiceEicarTests.ScanAsync_RealEicarInZipArchive_ReturnsThreatDetected`
  failed in a full local run on 2026-10-25 (T-F426's branch, no AMSI code touched); right after,
  both EICAR tests skipped with `AmsiScanBuffer failed (HRESULT 0x80070015)` (`ERROR_NOT_READY`): AMSI
  was not ready on the dev machine. `SkipIfTarOrAmsiUnavailableAttribute` probes AMSI when the test
  is discovered; AMSI stopped being ready between that probe and the scan. Next: decide what a scan that finds AMSI not ready means for these tests (skip at run
  time, or the result `AntivirusScanService` already gives a caller when AMSI is gone), with a test
  that turns AMSI off by a fake, not by changing the machine's antivirus. The failure's own message was not kept.
  2026-10-31: `Get-MpComputerStatus` reads real-time protection on again, so the real path can be
  checked; run both EICAR tests once to confirm before relying on it.
- **Reported by:** stream B's full run, 2026-10-25.

### T-F428 — A cancelled launch waits for the thread pool before tar.exe is killed (P3, from stream B)

- [ ] **Status:** fixed 2026-10-31, merged in #138 (`fix/stream-b-launcher-amsi`), awaiting the device check (stream E) (`docs/DECISIONS.md`, "T-F428"):
  a cancellation registration made after `ResumeThread` calls `TerminateProcess` on the cancelling
  thread. `Launcher_CancelWhileAReaderIsBusy_KillsTheChildWithoutTheReader` was red before (a reader
  busy in its callback kept PING alive past the cancel); `Launcher_StdInDataCancelled_ThrowsPromptly`
  now tells a child the cancel did not kill from a late return, with the pool's counts in the
  message. Hypothesis (2), the pool threads each launch blocks, moved to T-F445. History:
  `TarCommandLineEncodingTests.Launcher_StdInDataCancelled_ThrowsPromptly`
  took 59.6 s against its 30 s limit on run 40099759037 (2026-10-24, branch `docs/wave-1.9.0`, a plan
  PR with no code change), the lifetime of its `PING -n 60` child; a fuzz iteration timed out in the
  same run, so the whole runner may have stalled. Two things in `SandboxedProcessLauncher` make that
  possible, neither shown to be the cause: (1) the child is terminated only in `RunAsync`'s `catch`,
  after the stdout/stderr reads raise `OperationCanceledException`, and those reads are
  async-over-sync on pool threads (`CancelSynchronousIo`), so a cancel waits for pool progress;
  (2) each launch keeps two or three pool threads blocked for the child's whole life (both reads,
  `WriteStdIn`'s `Task.Run`), which a parallel test run multiplies. Candidate fix: a cancellation
  registration that calls `TerminateProcess` itself. Product exposure is low: in the App the reads
  start within milliseconds, so the I/O cancel lands. Product work in sandbox code: plan first.
  Before that, the test could tell "the cancel did not kill the child" from "the call returned
  late" (the child's PID through `OnProcessStarted`, `WaitForExit` on a dedicated thread, pool counts
  in the message).
- **Reported by:** stream B's survey of CI failures since 2026-10-18, 2026-10-25.

### T-F432 — Pakko's taskbar button shows no progress, as Explorer's copy does (P3)

- [ ] **Status:** fixed 2026-11-03, merged in #142 (`feat/tf432-taskbar-progress`), awaiting the device check (stream E) (`docs/DECISIONS.md`, "T-F432"):
  the App's button and tray icon and the operation window's button follow the whole batch; yellow
  while a prompt waits, red while a failed run's summary shows, cleared otherwise. `APP-031`,
  `APP-032` (with the tray redraw's cost) and `OPW-006` on the device in stream E.
- **Before:** open. While an operation runs, Explorer fills its taskbar button with a small
  progress bar under the icon; Pakko's does not. Wanted: the same under Pakko's icon
  (`ITaskbarList3::SetProgressValue`/`SetProgressState`; under Native AOT a `[GeneratedComInterface]`,
  never `[ComImport]`), cleared when the operation ends, and paused or error while a prompt or a
  failure waits for the user. Decide first: which windows (the Explorer operation window, the App,
  or both), and whether in a batch the button follows the current archive (as T-F431's speed does)
  or the whole batch (as Explorer's copy does).
- **2026-10-31, the user:** the App's tray icon (`H.NotifyIcon`, `MainWindow.xaml`'s `TrayIcon`) shows
  the progress too. Decided in the v1.10.0 wave: the App's button and tray icon and the operation
  window's button, all following the whole batch. Tray: an icon redrawn at a coarse step (e.g. every
  5 %) and the percent in its tooltip; measure the redraw cost first.
- **Reported by:** the user, 2026-10-29.

### T-F434 — "Move the sources to the Recycle Bin" keeps a clean source when another source of the run is skipped (P3)

- [ ] **Status:** fixed 2026-10-31, merged in #134 (`fix/tf434-recycle-clean-sources`), awaiting the device check (stream E) (`docs/DECISIONS.md`, "T-F434"): a
  skip keeps only the source it lies in; `APP-027`'s new fourth case on the device in stream E.
- **Before:** open. Package 1.9.0.2, the App (`APP-027`): a folder archived alone, unchanged, is
  recycled with no dialog. Archived together with a second folder that holds a junction (skipped), the
  first folder stays too and the dialog names both as not deleted; `pakko.log` says "Not deleted after
  operation" for it, with no "Output no longer on disk". Nothing is lost (the safe direction), but
  neither `docs/SPEC.md` nor the dialog says that one skipped source keeps the others. Because of it
  `APP-027` cannot tell an added or an edited file from this effect in the GUI. Next: read
  `SourceRecycler.RecycleLocalSources` with the run's `FullyProcessedSources` for a run with a skipped
  source; then either recycle each fully processed, unchanged source (a test per case) or write the
  rule down and say it in the dialog.
- **Reported by:** the release-tier smoke run of v1.9.0, 2026-10-30.

### T-F435 — "Some items were not deleted" gives "they may be in use or protected" also for an archive kept on purpose (P3)

- [ ] **Status:** fixed 2026-10-31, merged in #136 (`fix/tf435-kept-reasons`), awaiting the device check (stream E) (on top of T-F434): each kept
  source carries a `KeepReason` (`SourceRecycler`, App.Core) and the dialog groups the items under it:
  not processed in full, holds the new archive, a drive root, an entry replaced on disk, changed since
  the start, the output gone, not safe to delete (unresolvable path, or a link on a drive without a
  Recycle Bin), still on disk (in use or protected). Core names the three cases it decides after the
  run (`SourceResult.Cause`, a `PartialCause`); a skip, an error, a filter or a subset stays "not
  processed in full", since the summary already names those. 8 keys in 37 locales; the intro and the
  in-use sentence are the old `NotDeletedDialogMessage` split in two. `APP-028`/`APP-030` on the device
  in stream E. Not in scope: the CLI's `-sdel` lines keep their wording (Explorer's operations have no not-deleted report).
- **Before:** open. Package 1.9.0.2, the App (`APP-028`): an archive one of whose entries was
  overwritten by another entry is kept by design, and the dialog's only reason is the generic one (may
  be in use or protected). The same reason shows when the output is gone (`APP-030`). Wanted: the reason
  that applies, per item: kept because an entry overwrote another, the output is no longer on disk, or
  in use. Next: carry the reason with each kept item to the dialog; the texts in 37 locales.
- **Reported by:** the release-tier smoke run of v1.9.0, 2026-10-30.

### T-F437 — `Archiver.Shell.exe --paths-stdin` with a list it cannot read says only "The operation failed" (P3)

- [ ] **Status:** fixed 2026-10-31, merged in #135 (`fix/tf437-unreadable-path-list`), awaiting the device check (stream E): the message says the selection from
  File Explorer was not received (37 locales, `ResultSelectionNotReceived`), then `StdinPathList`'s reason in
  English; `SHL-006`'s second case on the device in stream E.
- **Before:** open. A harness that wrote the path list in UTF-8 instead of the UTF-16LE the
  extension writes (T-F235) got the list refused whole, rightly, but the message names no reason. Only a
  broken extension or a third-party caller can send such a list. Next: a message that says the path list
  could not be read; low priority.
- **Reported by:** the v1.9.0 release run, 2026-10-29.

### T-F439 — The App window's floor is not applied again on a move to a monitor of the same DPI with a smaller work area (P3, from T-F433)

- [ ] **Status:** fixed 2026-11-03, merged in #141 (`fix/tf439-floor-work-area`), awaiting the device check (stream E): `WindowFloorCache` keys the
  floor on the DPI and the work area's size, so a move to a monitor of the same DPI with another work
  area sets it again (`WindowFloorCacheTests`, 3 of 6 red with the DPI-only key); `APP-014` on the device
  in stream E.
- **Before:** open. `WindowFloor` caches the DPI and recomputes the floor only when it changes, so a
  window moved to a monitor of the same DPI whose work area is smaller keeps a floor that may not fit
  there (T-F433's "Not covered"). Next: recompute on a move between monitors (`AppWindow.Changed` with
  `DidPositionChange`, compare the monitor, not only the DPI), a `WindowFloorTests` case, `APP-014`.
- **Reported by:** T-F433's closing review, 2026-10-30.

### T-F440 — SonarCloud: 28 open findings on `main` after v1.9.0 (P2)

- [ ] **Status:** fixed, merged in #143 (`fix/tf440-sonar-main`). 39 on `main` by 2026-11-03 (streams A and C
  added 11): `S3903` by `ListingLayout.cs` with a red-first test (`CliAssemblyNamespaceTests`), the
  complexity, shadowing, ternary, `params`, literal, CSS motion and test-data findings fixed in place;
  `S3267` in `TarNames`, `xUnit1004` (T-F443) and the two `S1135` TODOs in `docs/CONVENTIONS.md`'s
  Won't-Fix list. Done when `main`'s scan shows only the two tracked `S1135` (kept as plain TODOs).
- **Before:** open. `issues/search` on `main`, 2026-10-31: 28 unresolved, one of type BUG
  (`S3903`, a type outside a namespace, `src/Archiver.CLI/Program.cs:1022`). Critical: `S3776` in
  `BareGzip.cs:61`, `BareGzipEngine.cs:72`, `Program.cs:862`, `scripts/smoke/Common.ps1:375`; `S3218`
  in `BareGzip.cs` (339, 341, 371, 373) and `IO/PathIdentity.cs:21`. Also `S107`
  (`BareGzipEngine.cs:185`), `S3358` (`ExtractionRouter.cs:82`), `S1192`, and test-side
  `IDE0028`/`xUnit1042`/`xUnit1045`. Most came with T-F413 (the bare gzip engine). Next: fix each, or
  record it in `docs/CONVENTIONS.md`'s Won't-Fix list with its reason; the full list from the API, not
  from this entry.
- **Reported by:** the v1.10.0 wave planning, 2026-10-31.

### T-F441 — One owner per text boundary: names and paths in every encoding Pakko meets, and a test that keeps it so (P2, user-requested, research first)

- [x] **Status:** done 2026-11-05: the last open step (`TarReader` in place of the walk) was checked
  against `dotnet/runtime` v10.0.14 and rejected, its precondition failed (`docs/DECISIONS.md`,
  "T-F441 and T-F424": UTF-8-only names, the doubled prefix separator, ~2 GB per forged header). The
  refusal's own message is T-F452; the folder case is T-F443.
- **Before:** in progress, branch `fix/tf441-text-boundaries`. Researched and planned 2026-10-31:
  `docs/DECISIONS.md` "T-F441 and T-F424" (both tar.exe builds measured; the silent case is two names
  that collapse to one listing line). Part 1 (code-page source, owners, the scanning test), then
  part 2 (the tar-name verdict). Follow-up, not in this task: the browse tree could show a plain or
  gzip tar's true names from its headers.
- **Done 2026-10-31:** part 1 merged (#130); part 2 green in CI (#132); the red check seen (draft
  #131, closed); the local full run green; `CLI-044` (`docs/SMOKE.md`) on this machine and the same
  commands on PakkoW10's tar.exe 3.5.2 with a Native AOT `pakko`. The folder case measured and moved
  to T-F443.
- **Red checks of the review's fixes, 2026-10-31:** each reverted on its own, its test failed: pax
  `size`, a pax `path` with a GNU long name, a prefix ending in `/`, a rooted name, .NET's own pages
  (3 cases), and hi-IN's fallback - so `GetLocaleInfoEx` does answer a sentinel for hi-IN.
- **Open before done:** `SECURITY.md` for the pax records now parsed in process (the user's OK 2026-10-31,
  written); the fresh reviewer (the user's OK 2026-10-31). Next step after part 2 merges (the user,
  2026-10-31): `TarHeaderNames.ReadNames` on `System.Formats.Tar`'s `TarReader`, names only (DECISIONS).
- **Before:** open, research first. The user, 2026-10-31: encoding bugs in names keep coming
  back; there must be one place (or a few, if needed) where names are resolved and checked, for
  tar.exe and for the whole Core, names and paths everywhere. History that justifies it: T-F204,
  T-F215, T-F234, T-F238, T-F244, T-F266, T-F283, T-F290, T-F305, T-F416, T-F418, T-F423, T-F424, and
  three tests that named a file the runner's code page could not carry (T-F342, T-F423, T-F290).
- **Several owners, not one decoder.** Each boundary uses its own code page on purpose, and folding
  them into one would be a new bug: tar.exe's command line and `-T`/exclude lists (system ANSI page,
  `TarCommandLineEncoding`); tar.exe's stdout (the user locale's ANSI page, `TarOutputEncoding`);
  tar headers (UTF-8 or OEM, `TarSandboxScope`/`TarHeaderNames`); ZIP legacy names (OEM/ANSI by host,
  `ZipNameCodePages`/`ZipEntryNameDecoder`); PAR2 names (UTF-8); the Shell's path list (UTF-16LE,
  `StdinPathList`, T-F437); the CLI console (`CliConsoleCharset`, `-scc`); the OperationUi pipe and
  launch arguments. Path rules (separators, `\\?\`, device names, trailing dot or space, case,
  identity) already have one pipeline (T-F420: `SourcePathNormalizer`, `EntryDiskName`,
  `ArchiveEntrySecurity`, `PathIdentity`); the inventory checks that nothing goes around it.
- **Target (to settle in the plan):**
  - (a) one source of this machine's code pages (ACP, OEMCP, the user locale's ANSI page), the only
    code that P/Invokes `GetACP`/`GetOEMCP`/`GetLocaleInfoEx` for them (four files do today), and
    injectable, as `ZipNameCodePages.FromCodePages` is;
  - (b) one tar-name boundary owning both directions (arguments and lists out; listing decode and
    T-F418's unescape in) and the one verdict "can this listed name be trusted", T-F416's `?` refusal
    moved into it; T-F424 is its first new case;
  - (c) ZIP, PAR2, console and the Shell list keep their own codecs but take pages from (a); an
    unreadable input says which boundary refused it (T-F437).
- **Open research question, not a mechanism:** a best-fit name cannot be seen from the listing alone
  (`café` lists as `cafe` in cp1251, and `cafe` round-trips through 1251 exactly). It needs the true
  name from elsewhere: the raw header bytes read in process (`TarHeaderNames` reads plain tar only
  today; the compressed filters?) or a second listing under another charset. Measure first.
- **Control:**
  - a source-scanning test, in the idiom of `AgentInstructionsSizeTests`/`SmokeCatalogueTests`,
    that fails when code-page selection (`GetACP`, `GetOEMCP`, code-page `GetLocaleInfoEx`,
    `CodePagesEncodingProvider`, `Encoding.GetEncoding(int)`, `Encoding.Default`) appears outside the
    owners, over `src/` and `tests/`; not `Encoding.UTF8`/`Unicode` for JSON, base64, PAR2 or wide
    Win32 buffers; the Latin1 byte probe in `TarSandboxedService` allowed by name;
  - tests with injected pages instead of the machine's: 1251, 1252, 932, 65001 (the "Unicode UTF-8"
    system setting) and a system page different from the user locale's;
  - one test helper for a non-ASCII name the runner's page can carry (from
    `tests/Archiver.Core.IntegrationTests/TarCodePage.cs`), used by every such fixture;
  - a smoke row with Cyrillic, CJK, `é` and a best-fit-prone name through every frontend.
- **Gate:** silent-costly class (untrusted-input parsing, an external data contract): plan mode, the
  design in `docs/DECISIONS.md` before code, advisor at both ends, the fresh-reviewer offer at the
  close. Not in scope: mojibake from editing source files, which `.claude/rules/text-encoding.md` owns.
- **Reported by:** the user, 2026-10-31.

### T-F442 — Formats tar.exe reads that Pakko does not offer: ISO, CAB, CPIO and others, by tar.exe version (P2, user-requested, stream H)

- [ ] **Status:** measured 2026-10-31 on both tar.exe builds (`docs/DECISIONS.md`, "T-F442"): ISO 9660,
  CAB, cpio, ar, WARC, tar.Z and uu/base64 read on both; xar and lzip on 3.8.9 only; mtree never.
  2026-11-05: plan approved (seven formats read-only; UDF ISOs refused), step 0 measured
  (`docs/DECISIONS.md`, "T-F442 - scope and the measurements before the code"). Next: step 2, Core
  detector and policy, with success and failure tests for every format and availability from the
  installed tar.exe (the user, 2026-11-05). Step 2 done on a branch: Core detection, capabilities,
  policy names, a cut ISO refused, both test classes `ReadOnlyTarFormatsTests`. CLI (`pakko i`, `-stx`)
  merged with the run on PakkoW10. xar left out of v1; Explorer menu and file types done, checked on
  the installed package here (`.iso`/`.cab` menu-only, `docs/DECISIONS.md`; SMOKE EXP-017). Docs done
  (POLICIES/ADMX, SPEC, README, site, SECURITY.md); the installed package's snapshot here names iso,
  cab, cpio, ar, compress and lzip, and a C++ test parses a 3.5.2-shaped line (no lzip) and hides
  `.tar.lz`. Fresh review 2026-11-06: a snapshot list without iso (an older Pakko's) is no longer
  current nor trusted by the menu, and the cut-ISO check closes the handle on a read error, each
  test seen red; the low findings are T-F457. Left for the release: the menu on PakkoW10, the CHANGELOG line with the BlockedFormats
  warning, the Store listing, and at the tag the "coming in v1.10.0 (not released yet)" markers
  dropped from `README.md`, `README.uk.md` and both site pages. The user, 2026-10-31: check extraction and the Explorer submenu
  for other archives that are not marked as Pakko's today (ISO, virtual machine disks, the list of a
  Store archiver: zip, rar, 7z, tar, wim, bz2, gz, xz, apfs, ar, arj, cab, cpio, cramfs, dmg, ext, fat,
  gpt, hfs, ihex, iso, lzh, lzma, mbr, nsis, ntfs, qcow2, rpm, squashfs, udf, uefi, vdi, vhd, vhdx, vmdk,
  xar, z, jar, 001, z01, crx); where tar.exe's support depends on its version, adjust at run time as
  the 7z writer (T-F342) and the readable formats (T-F403) already are.
- **Measure** on both tar.exe builds (Windows 11 bsdtar 3.8.9 here, Windows 10 3.5.2 on PakkoW10), a
  real file per format, each one "reads", "does not read" or "reads in part". libarchive's readers and
  filters make these the candidates: ISO 9660 (`.iso`), CAB, CPIO, ar (`.deb`), XAR (`.pkg`), LHA
  (`.lzh`), RPM, WARC, `.Z`, lz4, lzip, lzop, uuencode. `.jar` is a ZIP and goes the ZIP path; `.crx` is
  a ZIP behind a header, to be checked. Almost certainly outside tar.exe: VHD/VHDX/VMDK/QCOW2/VDI,
  DMG, WIM, APFS/ext/FAT/NTFS/HFS, SquashFS/cramfs, UDF, GPT/MBR, NSIS, ARJ, Intel HEX, UEFI, and split
  volumes (`.001`, `.z01`); those go into `docs/SPEC.md` as not supported, with the reason (Windows
  mounts VHD/VHDX and ISO itself).
- **What reads gets the whole set:** `ArchiveFormatDetector` by signature, not by extension only;
  `ArchiveFormatPolicy.TarReadableRegistryNames` and the Group Policy vocabulary (`docs/POLICIES.md`,
  ADMX); `TarCapabilitySnapshot`; the Explorer submenu (`ShellExtUtils.cpp`); file associations in the
  manifest; the App and `pakko`; Test (T-F397); `docs/SMOKE.md` rows.
- **Gate:** new untrusted-input formats, even inside tar.exe's sandbox: plan mode, advisor at both
  ends, `SECURITY.md` only with the user's word; a manifest change updates `docs/DIAGRAMS.md`; strings
  in 37 locales through the stream that holds that resource set.
- **Reported by:** the user, 2026-10-31.

### T-F443 — A folder whose name tar.exe lists by best fit leaves an empty twin, and an empty subfolder lands under the listed name (P3, from T-F441, stream A)

- [x] **Status:** fixed 2026-11-05 (`docs/DECISIONS.md`, "T-F443"): folder entries come from the
  quarantine when its folders disagree with the listing; the skipped test runs, with three more
  cases; both tar.exe builds measured; `CLI-044` extended. The merge of two true roots is T-F453.
- **Before:** open. Measured 2026-10-31 (bsdtar 3.8.9, ACP 1251, `Extract_Everything_FolderListedByBestFit_OnlyTheTrueFolder`,
  skipped until fixed): files land right, but folders are pre-created in the quarantine (T-F52), given
  their entries and their times by listed name, so the destination gets an empty `cafe/` beside the
  true accented folder, and an empty subfolder entry goes under `cafe/` instead of the true one.
  Next: map a listed folder name to the quarantine folder tar.exe wrote, through the same best fit
  Windows applies (`WideCharToMultiByte` without `WC_NO_BEST_FIT_CHARS` in the user's ANSI page, then
  back), and drop a pre-created folder that only a listed name made; measure on 3.5.2 too.
- **Reported by:** T-F441's measurement, 2026-10-31.

### T-F444 — "Scan for threats" with another antivirus instead of Defender, and with no antivirus at all (P2, user-requested, stream B, on the PakkoW10 VM)

- [ ] **Status:** fixed 2026-11-01, merged in #140 (`fix/tf444-amsi-non-s-ok`), awaiting the device check (stream E) (`docs/DECISIONS.md`,
  "T-F444"): Avira with real-time protection off answered `AmsiScanBuffer` with 0x00000426, a
  positive bare Win32 code, and NOT_DETECTED for EICAR; `AmsiScanner` checked `hr < 0`, so Pakko
  showed an archive holding EICAR as Clean. `AmsiScanner.InterpretScanResult` now takes only S_OK;
  anything else is `ScanEntryFailed`, Inconclusive. The new `InterpretScanResult_AnythingButSOk_Throws`
  cases for 0x426 and S_FALSE were red before; on the VM the same probe gives Inconclusive. Every
  other measured state (Avast, Avira, no subkey, a stale subkey, Defender's subkey without its
  engine, Avast's File Shield off) already gave the right verdict. SMOKE SHL-015, `SECURITY.md`
  ("Provider dependency", the user's OK). Not measured: Windows Server, `DisableAntiSpyware`, limited
  periodic scanning, Avast uninstalled again.
- **The problem as opened:** Pakko scans through AMSI (`AmsiScanner`, `AmsiScanBuffer`) and knows
  whether anything scans only from `HKLM\SOFTWARE\Microsoft\AMSI\Providers` having a subkey
  (`AmsiProviderCheck`, T-F146): no subkey means Inconclusive, never Clean. That was measured only with
  Defender active. Unknown: what a third-party antivirus does to that key and to the result. The risk
  is a Defender subkey that stays while Defender is passive or off. Then Pakko would count a provider
  that no longer scans, and a missed threat would read Clean instead of Inconclusive.
- **Questions to answer by measurement, not from memory:** (1) Does a free third-party antivirus
  (Avast Free, Avira Free; AVG if one of them registers no AMSI provider) register an AMSI provider,
  and under which GUID? (2) What happens to Defender's subkey and its state (`Get-MpComputerStatus`:
  `AMServiceEnabled`, `AMRunningMode` = Normal/Passive/EDR Block/disabled) once the other product is
  the registered antivirus in Windows Security? (3) What does Pakko show for EICAR (the T-F427 tests,
  plus `APP`/`EXP` scan rows) and for a clean archive in each state: Defender only, the other product
  only, both, the other product uninstalled again? (4) With no provider at all, is the verdict
  Inconclusive with its message (not Clean, not a crash)? (5) Can Defender be removed at all? Measure
  and record per edition: Windows 10/11 client (expected: it cannot be uninstalled, only turned off or
  passive under another product; N and LTSC editions still ship it), Windows Server (expected: a
  removable feature, `Uninstall-WindowsFeature Windows-Defender`); and whether turning it off by
  policy (`DisableAntiSpyware`, ignored on current client builds under Tamper Protection) removes its
  AMSI subkey.
- **Next:** a checkpoint of PakkoW10 first; nothing installed on the user's machine. Install through
  winget's `msstore` source (the user, 2026-10-31), checked 2026-10-31 with `winget show`: Avast One
  Free Antivirus `XPDNZJFNCR1B07` (publisher AVAST Software), Avira Security `XPFD23M0L795KD`
  (publisher Avira); `winget install --id <id> --source msstore --accept-package-agreements`, one
  product at a time, rolling back to the checkpoint between them. The `winget` source itself has
  neither (only ESET, Malwarebytes, Webroot, ClamAV and others). If PakkoW10 has no winget (App
  Installer), install App Installer from the Store first, or take the vendor's own installer and check
  its signature. Use the
  Native AOT `pakko` (`scripts\Publish-Cli.ps1 -Architecture x64`) and, where the App is needed,
  the MSIX. Record the measurements in `docs/DECISIONS.md` ("T-F444"). Then decide: if a stale
  subkey can give Clean, `AmsiProviderCheck` must check that the provider really scans (e.g. EICAR
  through `AmsiScanBuffer` gives DETECTED) before trusting a clean verdict (done instead: the strict
  S_OK check; the EICAR self-test was measured and rejected, `docs/DECISIONS.md` "T-F444"); `SECURITY.md` (with the
  user's OK), `docs/SMOKE.md` rows for the third-party case, and a Windows Server row if one is
  measured. Roll the VM back to the checkpoint afterwards.
- **Reported by:** the user, 2026-10-31.

### T-F445 — Each tar.exe launch blocks two or three pool threads for the child's whole life (P3, from T-F428)

- [ ] **Status:** open, not measured. `SandboxedProcessLauncher` reads stdout and stderr through
  `FileStream`s over synchronous pipe handles, so `ReadToEndAsync`/`ReadAsync` run as blocking reads
  on pool threads, and `WriteStdIn` holds a third through `Task.Run`. A parallel test run multiplies
  it; T-F428 took the cancel off this path, but a starved pool still delays a launch's return. Next:
  count pool threads during a parallel run of the sandbox tests; if it matters, overlapped pipes
  (`CreateNamedPipe` with `FILE_FLAG_OVERLAPPED`) or one dedicated thread per launch. Sandbox code:
  plan mode + advisor at both ends.
- **Reported by:** T-F428's hypothesis (2), 2026-10-31.

### T-F446 — An AMSI "blocked by admin" result reads as Clean (P3, from T-F444's fresh review)

- [ ] **Status:** fixed, merged in #144 (`fix/tf446-amsi-blocked-by-admin`), awaiting the device check (stream E) (2026-11-03): the range is
  Inconclusive with `ScanEntryBlockedByPolicy` (37 locales), red-first tests on its bounds and in the
  scan service; `docs/DECISIONS.md` "T-F446". No device row: no provider met so far (T-F444's
  three) answers in that range. Fresh `opus` reviewer (the user left it to the agent, 2026-11-03): no
  defects; the tar path has no blocked-entry test of its own, it shares `ScanOneEntryAsync` with ZIP.
  `SECURITY.md` names the cause (the user's OK, 2026-11-03).
- **Before:** open, not measured. `AmsiScanner` counts every `AMSI_RESULT` below 32768 as
  Clean, including `AMSI_RESULT_BLOCKED_BY_ADMIN_START..END` (16384-20479), which says an admin
  policy blocked the content, not that a scan found it clean (the code comment chose this on
  purpose, T-F146). Next: find out from the SDK's `amsi.h` and Microsoft's docs what a provider
  means by that range and whether any product returns it; if it is not a clean verdict, report it
  Inconclusive with its own reason (a new message code, 37 locales). Security boundary: plan mode,
  advisor at both ends, `SECURITY.md` only with the user's OK.
- **Reported by:** T-F444's fresh reviewer, 2026-11-01.

### T-F460 — "Scan for threats" reports Clean for a tar whose folder name ends in a space or a dot (P1, from T-F454)

- [ ] **Status:** fixed 2026-11-06 (#169, `fix/tf460-trailing-space-folder`), awaiting the device check
  (stream E: `SHL-017`) (`docs/DECISIONS.md`, "T-F460 and T-F454"): the quarantine walk reads through the long-path prefix and never skips a folder it
  cannot read; the same walk lost those files from extraction silently. On `main` and in v1.9.0.
  Put into stream B (the agent, 2026-11-06); ships with v1.10.0, no v1.9.x hotfix (the agent, as T-F448).
- **Reported by:** the agent, 2026-11-06, while measuring T-F454.

### T-F462 — "Clean" only when every listed file was scanned; extraction moves every listed file (P1, stream I)

- [ ] **Status:** fixed 2026-11-06 (`fix/tf462-scan-accounting`; `docs/DECISIONS.md`, "T-F462"), awaiting
  the device check (stream E: `SHL-018`); smoke `CLI-048`. Seen red on `main`: three same-named copies
  with EICAR in the middle scanned Clean.
- **Before:** open. Agreed 2026-11-06 (the user, "in this wave"). Root-cause review of T-F446,
  T-F448, T-F449, T-F460 and #169's fresh review: each false Clean was "found nothing bad" where it
  should be "proved everything was looked at", and each was closed on its own. One guard for the
  class: the tar scan counts the files it scanned against the listing's file entries plus the first
  copies (T-F171); any shortfall is Inconclusive naming the count, whatever name or folder broke.
  Extraction the same way: files committed plus files reported skipped equal the listing's files,
  or the run fails. Tests: each known shape (T-F460's folders, the first-copy pass, T-F455) with
  its own fix reverted must come out Inconclusive or failed, never Clean or success. Plan mode +
  advisor at both ends (the scan verdict); fresh opus reviewer offered once.
- **Reported by:** the agent's root-cause review, 2026-11-06.

### T-F463 — One owner for disk access in the tar quarantine, and a test that keeps it so (P2, stream I)

- [ ] **Status:** fixed 2026-11-06 (`fix/tf463-quarantine-owner`; `docs/DECISIONS.md`, "T-F463").
  The device check is stream E's `SHL-018`; the smoke row is `CLI-049`. Seen red on `main`: a threat in
  `a.txt.` beside a clean `a.txt` was named `a.txt`.
- **Left (measure):** creation copies a colliding source with `File.Copy(fullSource, ...)` by its
  plain name (`TarSandboxedService`, collision staging). Whether a source named `a.txt ` survives
  that is not measured.
- **Left (older than T-F463, from #172's fresh review):** `ArchiveEntrySecurity.IsReparsePoint`
  returns false on any exception (fails open); the walk never checks whether a *file* is a reparse
  point, so `OpenRead` would follow a file symlink (the pre-scan rejects symlink entries first); the
  walk does not observe cancellation.
- **Before:** open. Agreed 2026-11-06 (the user). T-F460 fixed eight calls that reached the
  quarantine without the long-path prefix (Windows trims `d ` to `d`); #169's fresh review found
  two more, one a false Clean. T-F420 (`EntryDiskName`) and T-F441 (`SystemCodePages`, `TarNames`,
  `TextBoundaryOwnersTests`) own the names; nothing owns the disk calls. Next: one place for every
  quarantine `Directory`/`File`/`FileInfo`/`FileStream` call (always `\\?\`), and a scanning test
  in the style of `TextBoundaryOwnersTests` that fails on a quarantine path used around it in
  `TarSandboxedService`, `Sandbox/` and `AntivirusScanService`.
- **Reported by:** the agent's root-cause review, 2026-11-06.

### T-F469 - A tar.exe 3.8.9 listing that leaves out an unreadable name refuses every selection (P3, stream I)

- [ ] **Status:** open (found by T-F467's exhaustive test, 2026-11-07). Where libarchive reads a raw
  header name only with the default character, 3.8.9 skips that entry ("Archive entry has empty or
  unreadable filename ... skipping", exit 1; measured on el-GR, lt-LT, he-IL, ar-SA). The listing then
  holds fewer names than the headers, and T-F441's count check refuses every selection and filter from
  that archive, including names that are fine. Decide whether the header walk can account for the
  skipped entries (same conversion, `TextBoundaries.TarRawHeaderNames`) so only they are refused; keep
  it fail closed. Both builds; `TarSendsAsListedExhaustiveTests` asserts today's behaviour.
- **Reported by:** the agent, 2026-11-07.

### T-F470 - A sandboxed helper over Windows' own `archiveint.dll` (P2, v1.10.0 stream I)

- [ ] **Status:** in progress, in the v1.10.0 wave (the user, 2026-11-07, first "an option for future
  releases", then the same day: "add the libarchive task to this wave; the constant file-name errors
  bother me"; lister and extractor, quiet fallback to tar.exe, `docs/DECISIONS.md`, "T-F470"). Step 1
  (measurements) and step 2 (`src/Archiver.ArchiveHelper`, packaging, protocol tests,
  `docs/ARCHITECTURE.md`'s protocol) done; Pakko does not launch it yet. Step 3 goes in two PRs: 3a
  (`refactor/tf470-reader-interface`) puts every tar-family read behind `ISandboxedArchiveReader` with
  tar.exe as its one backend, no behavior change, guarded by `SandboxedReaderOwnerTests`; 3b adds the
  helper backend and `ArchiveintGate` (sticky per process, helper path as a parameter) with listing
  and extract/Test/scan by index, and turns the gate on. Then step 4, T-F469/T-F453/T-F464/T-F461 narrowed to
  the tar.exe fallback, `docs/SMOKE.md`, `docs/TESTING.md`. Step 3 also verifies on the
  device a launch from the packaged App, Shell and `pakko` (identity proven), and reads the `V` line
  of CI's test job to see which libarchive its image runs. Measured
  in `docs/DECISIONS.md`, "T-F468 research done". The user's conditions:
  - only the DLL Windows ships (`C:\Windows\System32\archiveint.dll`, absolute path), with the
    signature checks tar.exe gets (T-F401's catalog case included);
  - the version read from the DLL (`archive_version_details`);
  - its capabilities decided at run time from that version and from probing, as T-F403 does for
    tar.exe.

  The shape the measurements point to:
  - a C helper importing only `kernel32.dll` (an AOT .NET helper fails with 0xC0000142 in the sandbox
    without a desktop);
  - copied into the quarantine before each launch (an exe in place in `WindowsApps` fails with error
    87);
  - names through `hdrcharset` set by the provider;
  - used as the lister first.

  The user OK'd the `CLAUDE.md` and `SECURITY.md` changes (2026-11-07; made in step 2). Plan mode and the advisor gate at both ends, plus a fresh reviewer (sandbox
  and untrusted-input class). Not measured yet: ARM64's `archiveint.dll`, and a launch from a process
  proven to hold package identity.
- **Reported by:** the user, 2026-11-07.

### T-F464 - The quarantine as the source of truth instead of predicting tar.exe's names (P3, stream I, research done)

- [ ] **Status:** research done 2026-11-07, decided in `docs/DECISIONS.md`, "T-F464 and T-F465". Code
  after T-F465, T-F466, T-F467 and T-F468. The quarantine's files are matched to the file entries in scope by the
  measured relation: the decoded listed name is the disk name or its best-fit round trip through the
  system ACP. Rules:
  - The match is in addition to T-F462's count, never in place of it.
  - Every quarantine file is scanned before anything is dropped (fresh review): the relation decides moves
    and the report, never what is scanned; on an unmeasured page or build a choice by name is Inconclusive.
  - A unique match is required wherever a name picks a file. These cases come from #170's review and
    from T-F461 item 5:
    - the filter's drop;
    - a duplicate group's first copy, judged per group;
    - the trimmed and renamed keying.
  - Anything ambiguous stays fail-closed.
  
  Tests first, on both builds. The unmeasured cases are listed in the entry.
- **Reported by:** the agent's root-cause review, 2026-11-06.

### T-F461 — tar folders ending in a dot or a space: the shapes still refused (P3, from T-F460)

- [ ] **Status:** open. Each fails loudly, none loses a file (`docs/DECISIONS.md`, "T-F460 and T-F454"):
  1. A file before its own `d /` entry: tar.exe fails on both builds whatever Pakko makes first.
  2. `d ./x.txt` with a filter: tar.exe also makes `d`, which the listing does not name; refused.
  3. Implicit `d../x.txt`: tar.exe asks for `d..`, Pakko makes `d`; the run fails.
  4. Explicit `d./` with a filter: the listing says `d`, tar.exe writes `d.`; refused.
  5. Explicit `d./` then `d./x.txt`, with `d\x.txt` already at the destination and Overwrite: the
     plan keys the trimmed name `d/x.txt`, the quarantine holds `d./x.txt`, so T-F419's "a trimmed name
     never replaces a file" misses and the file is replaced (ZIP keeps it apart). From #169's fresh review.
  Lead (3.8.9 only): a folder made by its literal name takes the file there. Making every pre-created
  folder literal fixes 2-4 but breaks a file followed by its `d./` entry unless that folder is not
  pre-created, which leans on tar.exe's implicit mkdir in the AppContainer (T-F52 saw it fail).
  Next: measure both on 3.5.2 and inside the sandbox before choosing.
- **Reported by:** the agent, 2026-11-06.

### T-F457 — Read-only formats: leftovers from T-F442's fresh review (P3)

- [ ] **Status:** open. Found 2026-11-06 by the fresh read-only review of T-F442; the two medium
  findings (a pre-T-F442 snapshot list hid the new formats in the menu; the cut-ISO check left the
  handle open on a read error) were fixed in T-F442's docs PR. Left, each low:
  1. An ISO inside a wrapper (`win.iso.gz`, `.cpio.Z`, `.cpio.lz`) reaches tar.exe as gzip/compress/
     lzip: no UDF refusal, no cut check, and `BlockedFormats=iso`/`cpio`/`ar` does not see it. Decide:
     document (policy names the outer format) or look inside.
     Done 2026-11-08 (`docs/DECISIONS.md`, "T-F457"; both builds measured): a gzip is looked into, and
     an ISO or UDF image comes out as the `.iso` file, judged when opened (smoke `CLI-045`). The other
     wrappers (no .NET decoder) and the outer policy name are documented in `docs/POLICIES.md`.
  2. The cut check refuses an image whose declared volume is a little longer than the file (a TAO
     CD-R dump missing the run-out sectors) that tar.exe reads in full. Measure a real one; maybe a
     tolerance.
     Done 2026-11-08: swept at every sector on both builds. Only a file under 24 sectors (libarchive
     does not bid; read as an empty tar, exit 0) or one ending before its root directory is refused.
     tar.exe reads the rest in full or fails by itself.
  3. Fixed 2026-11-06 (stream I): T-F398's name fallback takes `.tlz` too, so Slackware's tar.lzma
     `.tlz` is detected as LZMA and extracts (smoke `CLI-045`). Left: the menu still names `.tlz`
     `lzip` for `BlockedFormats`, so `BlockedFormats=lzip` hides a tar.lzma `.tlz` (C++, low).
  4. Fixed 2026-11-06 (stream I): `-stxudf` is a known type; a UDF image is `udf`, as Pakko detects
     it, so `-stxiso` leaves it alone (`docs/CLI.md`; smoke `CLI-045`).
  5. TOCTOU between `Detect(path)` and the sandbox scope reopening the path (older than T-F442): a file
     swapped in between passes `BlockedFormats`/the UDF refusal. Needs write access to the file.
- **Reported by:** the agent, 2026-11-06.

### T-F456 — `CancelInTheWindow_CancelsTheOperationWithoutFailover` times out under a full run (P3, flaky test)

- [ ] **Status:** open. Failed once in the full local run on 2026-11-05 (T-F443's branch, nothing
  near the Shell or OperationUi changed) after 16 s; 3 of 3 alone in about 0.3 s. Next: find which
  wait in `HelperOperationUiTests` races the loaded runner (T-F425's rule: no timer races).
- **2026-11-06:** not reproduced: 6 runs of `Archiver.Shell.Tests` beside `Archiver.Core.Tests`, all
  green. Rather than a guessed fix, every wait now names itself on timeout (`WaitUntilAsync` carries
  its condition through `CallerArgumentExpression`, the fake's read names the message it waited for,
  `BeginReadyAsync` says HelperBecameReady never ran); seen by forcing one condition false. Next
  failure: read the message, then fix that wait.
- **Reported by:** the agent, 2026-11-05.

### T-F455 — A tar folder entry stored without a trailing `/` arrives as nothing (P3, from T-F443's fresh review)

- [x] **Status:** fixed 2026-11-06 (`fix/tf455-slashless-folder`; `docs/DECISIONS.md`, "T-F455"): the
  scan appends the `/` to a name whose `-tvf` type is `d`; measured on 3.8.9 and 3.5.2, smoke
  `CLI-046` OK here and on PakkoW10 by hand. Put into the v1.10.0 wave's stream A (the agent's call,
  2026-11-06: a tar extraction bug on `main`, fixed before stream E's device checks).
- **Before:** open. Found 2026-11-05, on `main` too: a type-5 header named `emptydir` lists as
  `emptydir` (no slash), so the scan, a filter's `isFolder` and the folder entries all take it for a
  file; tar.exe makes the folder in the quarantine, the move walks files only, and the empty folder
  never reaches the destination. T-F443's verdict already reads such a name as a folder. Next: carry
  the listing's type into the names, without changing the member names given back to tar.exe (T-F05).
- **Reported by:** T-F443's fresh reviewer, 2026-11-05.

### T-F454 — A tar entry under a folder ending in a space, with no folder entry of its own, does not extract (P3, from T-F443's review)

- [x] **Status:** fixed 2026-11-06 with T-F460 (`docs/DECISIONS.md`, "T-F460 and T-F454"): Pakko makes
  the folder by tar.exe's name for it (`d ` keeps its space) through the long-path prefix; measured
  on 3.8.9 and 3.5.2, smoke `CLI-047`. The shapes still refused are T-F461.
- **Before:** open. Found 2026-11-05 (bsdtar 3.8.9, on `main` too): `d /x.txt` with no `d /`
  entry fails the whole run with tar.exe's "Can't create '...\out\d \x.txt': No such file or
  directory". Pakko pre-creates the folder (T-F52) without the long-path prefix, so Windows makes
  `d`, and tar.exe does not make `d ` itself. `d./x.txt` extracts. Next: pre-create through the
  long-path prefix, or measure whether pre-creation is still needed at all (T-F443 saw both tar.exe
  builds make an implicit folder themselves); 3.5.2 too.
- **Reported by:** T-F443's closing review, 2026-11-05.

### T-F453 — Two true root folders that tar.exe lists by one best-fit name merge when the root is dropped (P3, from T-F443)

- [ ] **Status:** open. Probed 2026-11-05 (bsdtar 3.8.9, ACP 1251): a tar with `caf<e-acute>/x.txt`
  and `cafe/y.txt`, named `cafe.tar`, extracted with `EliminateDuplicateRootFolder`: the listing shows
  one root `cafe`, the root is dropped, and `x.txt` and `y.txt` land side by side with no error.
  Without the drop both folders arrive. Next: when the quarantine disagrees with the listing
  (T-F443's verdict), decide the root shape on the quarantine's top level, or keep the root.
- **Reported by:** T-F443's probe, 2026-11-05.

### T-F451 — `RunPipelineAsync_CancelledMidFlight_NoTasksLeftRunningAfterwards` races a timer (P3, flaky test)

- [x] **Status:** fixed 2026-11-05: the third item to start cancels the run (seven still wait),
  no timer; `maxActiveObserved` by compare-and-swap. 15 of 15 alone, the full run green.
- **Before:** open. Failed once in a full local run on 2026-11-04 (T-F450's branch, nothing
  near the ZIP writer changed), then passed 5 of 5 alone. The test cancels after `Task.Delay(20)`
  while 10 items of 50 ms each run three at a time: under load the delay can outlast the whole
  pipeline, so no `OperationCanceledException` comes (cause inferred, the failing assertion was not
  captured). T-F425's rule: never race a timer; cancel from inside `compressItem` once a few items
  have started, and keep `maxActiveObserved` with an atomic max.
- **Reported by:** the agent, 2026-11-04.

### T-F450 — A timed-out `tar.exe --version` probe reads as an old tar.exe (P2, CI flake on #149)

- [x] **Status:** fixed 2026-11-04 (`docs/DECISIONS.md`, "T-F450"): Shell and `pakko` ask through the
  patient probe, an empty version has its own reason; the App and the menu snapshot keep the quick
  probe. No on-device check: a tar.exe slower than five seconds cannot be produced on demand, and
  the unit tests pin both sides (the T-F446 precedent). Before: seen 2026-11-04 in CI on PR #149 (a test-only change): two
  `CliSevenZipSubprocessTests` failed with "7-Zip requires tar.exe with libarchive >= 3.7.0 ...;
  this system's tar.exe (version ) ...", an empty version, on a loaded runner (the CLI tests took
  8 minutes). `TarSandboxedService.DetectCapabilitiesAsync` gives up after `DetectionTimeout` (5 s)
  and any failure becomes `new TarCapabilities()`, so a slow probe is reported as a tar.exe too old
  for 7z, the wrong reason, on a user's slow machine as well. T-F342 already separated "the 7z
  writer probe got no answer" from "no writer"; the version probe needs the same: a probe that did
  not answer is its own state with its own message (and maybe one retry), never "old tar.exe".
- **Reported by:** the agent, from CI run 41887492188, 2026-11-04.

### T-F449 — A scan of no archives at all reports Clean (P3, from T-F448's review)

- [x] **Status:** fixed 2026-11-04 (in the v1.10.0 wave, the user): no archive given is Inconclusive
  with no findings; an archive with no entries stays Clean (both pinned). Before: `AntivirusScanService.ScanAsync` with an empty `ArchivePaths` returned
  `OverallVerdict = Clean` with no findings (`BuildResult` over an empty list), "nothing found" for
  nothing scanned. No frontend is known to pass an empty list; decide between Inconclusive and an
  argument error at entry, then pin it in `CoreNeverThrowsContractTests`/the service tests.
- **Reported by:** the fresh `opus` review of T-F448, 2026-11-04.

### T-F448 — With Trend Micro as the antivirus, an archive holding EICAR scans Clean (P1, from T-F447)

- [x] **Status:** fixed 2026-11-04, `SHL-016` OK on PakkoW10 2026-11-05 (`docs/DECISIONS.md`, "T-F448"):
  a provider on the measured list, alone, makes every archive Inconclusive with a reason of its own;
  re-measure when Trend ships a new version or a business product is at hand. Before: open, measured 2026-11-04 (`docs/DECISIONS.md`, "T-F447"). Trend Micro Maximum
  Security 17.9 installed in full on PakkoW10 turns Defender off and leaves its own AMSI provider
  the only one; that provider answers S_OK with AMSI_RESULT_CLEAN (0) for EICAR and for clean text,
  whatever the app name, so `AmsiScanner` reports Clean. Pakko trusts any S_OK below 32768, and
  "Clean" then says only that the provider reported nothing. Whether Trend's provider finds real
  malicious scripts was not measured; it does not find EICAR or Microsoft's AMSI test sample.
  Ruled out already: an EICAR self-test per scan (Defender logs a severe detection each time,
  T-F444/T-F447) and Security Center (absent on Server, no state map; Trend shows 0x041000, "on").
  Next: plan mode + advisor at both ends (a security boundary), `SECURITY.md` only with the user's
  OK. Candidates to weigh, not chosen: name the provider in the result ("nothing found by Trend
  Micro", from the CLSID's name) so Clean never reads as Pakko's own guarantee; a one-off check per
  provider CLSID and DLL version cached, which alerts once per product update instead of per scan;
  a list of providers known not to detect EICAR whose Clean becomes Inconclusive. Confirmed on the
  shipped path: the signed package 1.9.0.2 is not blocked by Trend, and its Explorer "Scan for
  threats" on an EICAR ZIP showed "nothing found" (the user, 2026-11-04).
- **Reported by:** T-F447's measurement with the user's clicks on the VM, 2026-11-04.

### T-F447 — More AMSI providers on the PakkoW10 VM, through winget, no Russian products (P2, user-requested, stream B)

- [ ] **Status:** measured 2026-11-03, no code change (`docs/DECISIONS.md`, "T-F447"): AVG replaces
  Defender and detects; ESET (not activated) and Trend Micro (setup unfinished) add a provider beside
  Defender's, which still detects; on its own each answers NOT_DETECTED or CLEAN for EICAR, a state
  Windows did not produce. Huorong registers no provider: Inconclusive. Malwarebytes, Webroot and
  360 register none and leave Defender on. EICAR self-test re-measured and still rejected (Defender's
  severe 1116/1117 per scan). Bitdefender (setup clicked through by the user, 2026-11-04): its
  provider appears only after a user logon, Inconclusive until then, then detects. Next, if the
  screen is free: TotalAV (download unreachable). Trend Micro installed in full (2026-11-04): its
  provider answers Clean for EICAR, T-F448. ESET activated (trial, 2026-11-04): replaces Defender
  and detects.
- **Reported by:** the user, 2026-11-03.

### T-F393 — The About button went disabled once and no dialog came (P3)

- [ ] **Status:** deferred past 1.9.0 (2026-10-29, agent: P3, no repro). Package 1.8.1.19, uk-UA: About opened
  25 times out of 25 - 15 in one window through UI Automation's Invoke (the dialog in ~140 ms, the button
  disabled while it is open, enabled after), and 10 cold starts of `Archiver.App.exe` with a topmost window
  over the window's right half, About invoked as soon as the window had a handle (dialog in 139-159 ms).
  Earlier: package 1.8.1.5 (`b146f3b`), uk-UA, 2026-10-12. The App was started with
  `Start-Process` on `Archiver.App.exe` in the install folder; a topmost Windows Terminal window
  covered the right half of its window, the About button included. One UI Automation click on
  About: the button reads disabled from then on, no dialog is in the UI Automation tree or on a
  picture of the window taken after the terminal was minimized, `pakko.log` has "Pakko started"
  only, the Application event log has nothing. The window was closed after about two minutes.
  Three more tries (one under the same topmost window) opened the dialog. So `ShowAboutAsync`
  was entered and never finished, without a dialog on screen. `DialogService` has no gate of
  its own: every dialog calls `ContentDialog.ShowAsync` directly, and WinUI allows one open
  dialog per window. Not tried in that window: whether a second dialog (a name conflict, a
  password prompt) would still open; if WinUI counted the About dialog as open, it would not.
  Next: reproduce, then try a second dialog in the stuck window. Does not hold the v1.8.1 tag:
  seen once in five tries, with no known trigger.
- **Reported by:** the release-tier smoke run of v1.8.1 (`APP-001`), 2026-10-12.

