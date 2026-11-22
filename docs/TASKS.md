# TASKS.md — Active and Future Tasks

> Open work only: the v1.12.0 wave first, then the future tasks. Finished tasks live in
> [`TASKS_DONE.md`](TASKS_DONE.md); each graduation below names its section there.
>
> **Graduated 2026-11-20 (after v1.11.0):** section "Graduated 2026-11-20 (after v1.11.0)" of
> [`TASKS_DONE.md`](TASKS_DONE.md): the v1.11.0 wave plan; the tasks graduated that day are appended
> after it with their own "Graduated 2026-11-20" line.
>
> **Graduated 2026-11-11 (after v1.10.0):** section "Graduated 2026-11-11 (after v1.10.0)" of
> [`TASKS_DONE.md`](TASKS_DONE.md): the v1.10.0 wave plan, T-F460, T-F462, T-F479, T-F470.
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

## v1.12.0 wave - tar.exe in its sandbox made fast, and the open tasks (planned 2026-11-20)

The user, 2026-11-20: the next release works on the speed of what goes through the sandbox (tar,
7z and the other formats tar.exe handles), multi-threading and tar.exe's options, plus every open
task. The first measure (`docs/DECISIONS.md`, "Pakko against NanaZip and 7-Zip") put tar and 7z
2 to 12 times behind 7-Zip; the bare tar.exe extracts the same 3,619-file tar in 1.6 s where Pakko
takes 16.5 s, so most of it is Pakko's own. Creating tar, tar.gz and tar.zst through .NET (T-F514)
was a real candidate (spiked, rejected 2026-11-21); reading stays in the sandbox. Plan and inventory: `docs/DECISIONS.md`,
"v1.12.0 wave plan".

Same rules as the v1.11.0 wave: one branch and one PR per batch, one commit per task, Sonar's rules
checked before the push; a speed change is measured before and after with
`scripts/Measure-Comparison.ps1` on the same data, real-time protection on, on a Dev Drive and on
plain NTFS, and against 7-Zip at the same archive size, never the same `-mx`; every change to the tar
path runs on both tar builds (3.8.x here, 3.5.2 on PakkoW10); the sandbox boundary gets an opus
reviewer at the batch's close, with the user's OK.

| # | Batch | Tasks | Checked once per batch |
|---|---|---|---|
| 1 | Where the time goes | T-F565 step 1: bare tar.exe and the helper against Pakko on the same archives; Pakko's phases (scope, profile, scans, listings, the move out of the quarantine, the commit) | A table in DECISIONS, both volumes, both tar builds; it decides what batches 2 and 3 hold |
| 2 | Extraction through the sandbox | T-F565 (extraction), T-F561 (reproduced as P2 and fixed 2026-11-21), T-F567 (closed by measurement 2026-11-21), T-F560 (closed, owner found 2026-11-21); T-F464 if batch 1 shows the second listing or the name prediction costs | Measured before and after, the quiet suite, both tar builds, an opus reviewer |
| 3 | Creation | T-F565 (creation: libarchive's options, threads where a filter has them); T-F514 (`TarWriter` with `GZipStream`/`ZstandardStream` for tar, tar.gz, tar.zst; spiked and rejected 2026-11-21, tar.exe stays; `TarReader` read again at the SDK's 11.0.x tag for the three faults that rejected it on 10.0.14) | Same archive size against 7-Zip; every archive read by 7-Zip and both tar builds; `HardLinkMode` against T-F392; `SECURITY.md` only with the user's OK |
| 4 | ARM64 helper | T-F562 | `canary-arm64` native; one AppContainer run on an ARM64 machine if there is one |
| 5 | Names | T-F556, T-F563, T-F559 | One deploy, `SHL-019`, PakkoW10 for T-F559 |
| 6 | Taskbar, the file list | T-F564, T-F566 | `APP-031`, `OPW-006` and T-F566's new APP row on the device |
| 7 | A damaged entry kept | T-F496 | The failure matrix (CRC, length, deflate) at every test level, one deploy |
| 8 | Device, user, release | T-F432 (`APP-032` by eye), T-F494, T-F444, T-F446, T-F447, T-F254, T-F508, T-F317 (only with the user's OK); `STO-005`, `STO-006`, `STO-008` for 1.11.0 after certification; the release tier; the tag after the user's OK | As batch 8 of v1.11.0 |

Order: this plan, then batch 1, then 2 and 3; 4 to 7 between them while no long series runs; 8 last.

Not in this wave, unchanged: T-F445 (P4, moved out of batch 2 on 2026-11-21: Pakko still runs one
launch at a time, its revisit condition); T-F453, T-F461, T-F469 (P4, the tar.exe fallback; touched only if batch
2 changes the same code); T-F02, T-F10, T-F91, T-F119, T-F121, T-F127, T-F132, T-F144, T-F152, T-F202,
T-F328, T-F329, T-F331, T-F332, T-F359, T-F393.
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
- **2026-11-11 (v1.11.0 stream F, decided by the agent on the user's "your call"):** #449503 is
  still open and validated, untouched by a moderator since 2026-10-09. No second outward PR while
  it waits; at the v1.11.0 release (stream E) its manifest moves to 1.11.0 (the .NET 11 build), or,
  if it has merged by then, a version-update PR for 1.11.0 follows.
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

### T-F445 — Each tar.exe launch blocks two or three pool threads for the child's whole life (P4, from T-F428)

- [ ] **Status:** open, P4, out of the v1.12.0 wave (2026-11-21): no code in `src` starts two sandboxed
  launches at once (one archive at a time; batch 2's step 2, threads for the move out, was rejected), so the
  condition below is still unmet and the change would be sandbox code for a test run's sake only.
  Measured 2026-11-08, no code for v1.10.0. A temporary test listed a tar.gz of
  150,000 entries through `TarSandboxedService.ListEntriesAsync`, N launches at once, while a
  dedicated thread sampled the pool and the start delay of a queued work item (12 CPUs, minimum 12
  workers):

  | Launches at once | Pool threads | Longest start delay |
  |---|---|---|
  | 1 (what an operation does) | 11, unchanged | 0 ms |
  | 8, pool already grown | 41, unchanged | 9 ms |
  | 16, cold pool | 20 -> 41 | 6.2 s |
  | 16, six 256 MB zero entries (little output, 1 s) | 11 -> 20 | 0.4 s |

  The delay is the pool's injection ramp under many launches, with the listing's own parsing on the
  same pool; one operation reads one archive at a time, so only a parallel test run meets it (as
  T-F428 and T-F425 did). P4: revisit if Pakko ever runs launches in parallel.
- **Before:** open, not measured. `SandboxedProcessLauncher` reads stdout and stderr through
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

### T-F494 - The device checks the v1.10.0 release did not walk (P3, v1.11.0 stream D)

- [ ] **Status:** open (`docs/DECISIONS.md`, "v1.10.0 - the release record"): the Explorer menu on
  PakkoW10 for `CLI-032`/`CLI-035` (a background task cannot open it and the Hyper-V console runs
  elevated, out of the `windows` MCP server's reach: find a non-elevated way in, such as an
  enhanced session as `tester`); `PKG-010`'s "Show more options" menu; the archive helper on ARM64
  (measured 2026-11-20 and taken there by T-F562; left: `STO-008` on a real ARM64 machine); `REL-001`
  run as written (an elevated `testhost` held `Archiver.App.Core.Tests`' bin, so the release ran it
  project by project).
- **Reported by:** the agent, 2026-11-11.

### T-F496 - Keep a damaged file on request: what is left of an entry whose data or CRC-32 is bad (P3, user-requested, after v1.11.0)

- [ ] **Status:** open, not before v1.11.0 ships. The user, 2026-11-11: an archive with one bad block
  still holds data someone may need at any cost. Today a ZIP entry that fails its CRC-32 (T-F246),
  runs longer than declared (T-F231) or has a damaged deflate stream fails alone, the other entries
  are extracted, and its half-written file is deleted (`CopyEntryToDestinationAsync`, T-F230). .NET 11
  checks the CRC too (T-F492); that changed the message's source, not the outcome. What a kept file
  can hold: a CRC mismatch only - the whole decoded data (a flipped bit in a stored entry, or a bad
  CRC field); a deflate stream broken midway - the bytes before the break (deflate does not
  resynchronise after it); AES - the same, decryption is per block. A damaged central directory
  stays out of reach: the BCL does not scan local headers, and a scanner of our own is not this task.
  WinRAR has "Keep broken files", 7-Zip keeps them by default.
  **Guards, each a test, so the option never becomes a hole in a later release:**
  1. Off by default and per operation only: an explicit switch in the App's extract dialog and
     `pakko x` (`-kb` or similar, checked against 7-Zip's switches first), never remembered as a
     setting, never on for Explorer's one-click "Extract here". A Group Policy can forbid it.
  2. A kept file is never mistaken for a good one: a suffix in its name (e.g. `name.damaged.ext`,
     so no program opens it by its old association), the operation `Partial` with the reason per
     entry, exit code 2 in the CLI, the log line.
  3. Every other guard runs unchanged before and while the file is written: path traversal,
     reserved names, ADS, reparse points, the compression-ratio and disk-space checks (a damaged
     entry's declared size is untrusted; the bytes written stay capped by it, T-F231), Mark of the
     Web on the kept file.
  4. Only the decoded bytes: nothing is guessed, padded or zero-filled past the break.
  5. Encrypted entries: a file is kept only if the password was verified (AES's HMAC failing on a
     damaged block is the damage; a wrong password is not and keeps nothing). Open: Pakko's
     reader, kept by T-F493 (batch 7), checks the HMAC before any byte and returns no stream when
     it fails (`EncryptedZipEntryReader`), so a damaged AES entry has nothing to keep until this
     task gives the reader a path that decodes it anyway; decide that here.
  6. Test, Hash, preview and the AMSI scan never use it: they report the damage as today.
  7. tar, 7z, RAR and the read-only formats: first measure what the archive helper and `tar.exe`
     leave on disk for a damaged entry today, on both tar builds (Win11 3.8.x and PakkoW10 3.5.2);
     the same guards apply, or the option is ZIP-only and says so.
  Before code: plan mode + advisor (irreversible writes from untrusted input), tests of every guard
  seen red first, a `docs/SMOKE.md` row, and the trade-off in `SECURITY.md` (the user's OK for that edit: 2026-11-11, "Дозвіл даю"). The answer for an archive that must come back whole stays PAR2 (`pakko r`).
- **Reported by:** the user, 2026-11-11.

### T-F508 - The litmus paper that stays, and the device-side candidates (P3, from T-F498)

- [~] **Status:** step 1 done 2026-11-12 (`LitmusPaperTests`, Slow, in the canary; `docs/DECISIONS.md`, "T-F508 step 1"). Left: five canary nights, then a gate; the two device checks, batched with stream E's on-device checks (one Deploy.ps1 for both, the agent's decision 2026-11-12).
  1. A Slow-tagged check that runs the litmus tool for zip-optimal and zip-fastest over 2,000 files
     and fails when faults per entry exceed, say, 15x the floor (today 3-4x, 68x before T-F318).
     Gate it only after five canary runs show it stable on the runners.
  2. On a device:
     - the App's sort over 2,000 rows (`ApplySort`, `IndexOf` plus `Move` per row, O(N^2));
     - Explorer's right-click with 2,000 files selected (the shell extension resolves the selection
       in each of about 18 `GetState`/`GetTitle` calls).

     Measure both, and fix only what is felt.
- **Reported by:** the agent (T-F498), 2026-11-11. **Scheduled:** v1.11.0, stream N (the user, 2026-11-11: every task found in T-F497/T-F498 goes in this wave).

### T-F566 - The file list's per-row removal cannot be seen (P3, batch 6)

- [ ] **Status:** open. The user, 2026-11-21: in the App's list of files to archive only "Clear"
  shows; a single file or folder seems impossible to take out. It is possible, but hidden: the
  row's right-click menu ("Remove from list", `RemoveMenuItem`), Shift+F10 or the menu key, and
  Delete on a focused row (`PendingList_PreviewKeyDown`, which also moves focus to the next row).
  Next: a small trailing button on each row (`E711` or `E74D`):
  - a sixth column in the row template's `ColumnDefinitions` and the same in the header grid
    above it, so the headers stay aligned;
  - one path: a thin click handler calls `RemovePath` and `FocusRowLater` as Delete does, enabled
    only while `ClearCommand` can run (greyed out during an operation, as the menu and the key);
  - shown always, or on pointer-over and while the row has focus (never hover alone: touch and
    keyboard); `IsTabStop="False"` (Delete and the menu key cover the keyboard, a stop per row
    makes a long list slow to tab through), but in UI Automation with `AutomationProperties.Name`
    "Remove <name>" and a matching tooltip;
  - a new string in 37 locales (`Glossary.tsv` first, `RemoveMenuItem`'s wording where it fits),
    through a `py -3` script;
  - a `docs/SMOKE.md` APP row: mouse, touch or pen where a device has one, Narrator, disabled
    during an operation, focus after removing the last row.
  Not in it: the Archive Browser's list, which has its own selection.
- **Reported by:** the user, 2026-11-21.

### T-F565 - tar and 7z through tar.exe take 2 to 12 times 7-Zip's time (P3, next release)

- [ ] **Status:** open. Measured 2026-11-20 (`docs/DECISIONS.md`, "Pakko against NanaZip and
  7-Zip"): extracting a tar of the 3,619-file .NET SDK took Pakko 14.8 s against NanaZip's 2.0 s
  and 7za's 7.7 s, a 7z of it 17 s against 2.6-4.5 s, one large file in a 7z 7 s against 1-2 s;
  a ZIP of the same files extracts in NanaZip's time. Creating a 7z is slower still (3 to 12
  times), but that is tar.exe's one-thread LZMA, not ours. Next: split the time of one extraction
  into the sandbox's start, the unsafe-entry scan, tar.exe or the helper, and placing the files,
  then decide what can go. Not a regression: v1.10.0 was not measured this way.
- **2026-11-20, the user:** no figures in the Store listing for now; the next release works on the
  sandbox around tar.exe, multi-threading and tar.exe's other options, creation included (libarchive's
  per-format `--options`, e.g. threads for xz and zstd, where its 7z writer has none). Measure with
  the same data and script as the record, comparing at the same archive size, not the same `-mx`.
- **2026-11-21, the user (batch 3):** zstd becomes a 7z method the user picks, with a note that
  7-Zip does not open it (7za 26.02: "Unsupported Method"; Windows 11's tar.exe reads it), offered
  only where a probe of tar.exe shows it writes zstd in 7z (libarchive 3.8.0+), LZMA2 otherwise;
  it is not the default of Explorer's "Add to name.7z". xz and zstd in tar get libarchive's threads
  by default (measured 6 times faster for xz, the same size for zstd), bounded by memory. No
  "Add to name.tar.zst" item in the submenu.
- **2026-11-21, the user, later:** Zstandard in 7z dropped after it was built ("not worth it");
  threads for xz and zstd in tar stay. Why: `docs/DECISIONS.md`, "v1.12.0 batch 3, steps 1-3".
- **Reported by:** the agent, from the user's request for Store figures, 2026-11-20.
### T-F564 - The taskbar button shows no yellow or red at 0 % (P3, batch 8)

- [ ] **Status:** open. Found 2026-11-20 by `APP-031`/`OPW-006` (T-F432) on 1.10.0.40, Windows 11
  26300. An operation that waits for a conflict answer or fails while its percent is still 0 shows
  only the grey track: a 4 KB damaged ZIP failing at once, and a single-file extraction asking
  "the file exists", in both the App and the operation window. Near 100 % red is full width, so the
  state reaches the button; the value does not. `OperationIndicator.Finished` keeps `_percent` and
  `TaskbarButton.Apply` sets the value, then the state. Next: Error and Paused shown whatever the
  percent (for instance the value at least a small visible part, or 100 for Error), in
  `OperationIndicator` and `OperationWindowModel.IndicatorState` alike, test first.
- **Reported by:** the agent (batch 8), 2026-11-20.

### T-F562 - The archive helper on ARM64 (P3, batch 8)

- [~] **Status:** fixed on `batch-4-arm64-helper` (2026-11-21): `ArchiveintGate` admits a native
  ARM64 process with an ARM64 helper (and only a helper built for the process's machine);
  `canary-arm64` builds the ARM64 helper and runs the helper tests in an AppContainer there
  (`docs/DECISIONS.md`, "T-F562"). Left: `STO-008` on a real ARM64 machine. Before: Measured 2026-11-20 on the `windows-11-arm` runner: Windows on ARM64 ships
  its own `C:\Windows\System32\archiveint.dll` (libarchive 3.8.9, the same codecs as x64; the
  "no ARM64 build" in `docs/DECISIONS.md`, "T-F470", was a gap in Winbindex). A native ARM64
  `Archiver.ArchiveHelper.exe` outside an AppContainer: `probe` exit 0, every format, and a tar.gz
  listed. v1.11.0 ships unchanged: `ArchiveintGate` sends ARM64 to tar.exe. Next: the gate admits
  native ARM64, the MSIX carries the ARM64 helper, the helper tests run native on `canary-arm64`
  (`NativeFact`), and one run in an AppContainer on a real ARM64 machine.
- **Reported by:** the agent (batch 8), 2026-11-20.

### T-F561 - tar.exe wrote more than its listing declared: a 7 KB gzip filled 4 GiB (P2, batch 6's review)

- [~] **Status:** fixed on `fix/tf561-write-cap` (2026-11-21); `CLI-073` passed on 1.11.0.4, the 4 GiB
  file stopped in 1.1 s with nothing written; the App's and Explorer's view of the error waits for batch 8.
  The premise "no such format is reachable" was wrong: a gzip (or any filter tar.exe reads) holding a
  ZIP written as a stream, its sizes in data descriptors after the data, is detected as GZip and read
  by tar.exe, whose streaming ZIP reader lists the entry as 0 bytes, so T-F94's ratio check saw 0.
  Measured on 3.8.9 and on PakkoW10's 3.5.2: `pakko x` of a 65 KB file wrote 64 MiB, exit 0; a 6.9 KB
  one is 4 GiB, written at about 1.3 GB/s; the job's CPU limit (an hour at least) would allow terabytes.
  Fix: every `-x` run (extraction, the first copies, Test's `-O`, the scan's) is held by
  `JobWriteLimit` to the declared bytes of what it extracts: the job's own `WriteTransferCount`
  less the stderr Pakko read, over the declared bytes and 1 MiB, stops the job; the run fails with
  `ContentLargerThanDeclared`. `docs/DECISIONS.md`, "T-F561".
- **Before:** P3. T-F554 refuses a `-tv` line whose size does not parse, but the bomb check
  (`ArchiveEntrySecurity.EvaluateCompressionBombAsync`) trusted the sizes libarchive prints, and
  nothing capped what tar.exe then wrote.
- **Reported by:** the batch 6 closing reviewer, 2026-11-18; reproduced by the agent, 2026-11-21.

### T-F469 - A tar.exe 3.8.9 listing that leaves out an unreadable name refuses every selection (P4, stream I, the tar.exe fallback)

- [ ] **Status:** open (found by T-F467's exhaustive test, 2026-11-07). Where libarchive reads a raw
  header name only with the default character, 3.8.9 skips that entry ("Archive entry has empty or
  unreadable filename ... skipping", exit 1; measured on el-GR, lt-LT, he-IL, ar-SA). The listing then
  holds fewer names than the headers, and T-F441's count check refuses every selection and filter from
  that archive, including names that are fine. Decide whether the header walk can account for the
  skipped entries (same conversion, `TextBoundaries.TarRawHeaderNames`) so only they are refused; keep
  it fail closed. Both builds; `TarSendsAsListedExhaustiveTests` asserts today's behaviour.
- **Scope since 2026-11-08 (T-F470 step 4):** the tar.exe fallback only. Where `ArchiveintGate`
  chooses the archive helper (native x64 or ARM64, a signed `archiveint.dll` of libarchive 3.5 to 3.8,
  the helper beside Pakko) names come from the helper by index, never through tar.exe's listing or its
  writes. This is left for an x64 process under emulation, a libarchive version not measured,
  and a build or run without the helper (`pakko i` and the App log name the reader; smoke `CLI-050`,
  `CLI-051`, `APP-037`). P4: the fallback reaches few machines. The same holds for T-F464, T-F461 and T-F453.
- **Reported by:** the agent, 2026-11-07.

### T-F464 - The quarantine as the source of truth instead of predicting tar.exe's names (P4, stream I, research done, the tar.exe fallback)

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
- **Scope since 2026-11-08 (T-F470 step 4):** the tar.exe fallback only, as T-F469 says. P4.
- **Reported by:** the agent's root-cause review, 2026-11-06.

### T-F461 — tar folders ending in a dot or a space: the shapes still refused (P4, from T-F460, the tar.exe fallback)

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
- **Scope since 2026-11-08 (T-F470 step 4):** the tar.exe fallback only, as T-F469 says. P4.
- **Reported by:** the agent, 2026-11-06.

### T-F453 — Two true root folders that tar.exe lists by one best-fit name merge when the root is dropped (P4, from T-F443, the tar.exe fallback)

- [ ] **Status:** open. Probed 2026-11-05 (bsdtar 3.8.9, ACP 1251): a tar with `caf<e-acute>/x.txt`
  and `cafe/y.txt`, named `cafe.tar`, extracted with `EliminateDuplicateRootFolder`: the listing shows
  one root `cafe`, the root is dropped, and `x.txt` and `y.txt` land side by side with no error.
  Without the drop both folders arrive. Next: when the quarantine disagrees with the listing
  (T-F443's verdict), decide the root shape on the quarantine's top level, or keep the root.
- **Scope since 2026-11-08 (T-F470 step 4):** the tar.exe fallback only, as T-F469 says. P4.
- **Reported by:** T-F443's probe, 2026-11-05.

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

