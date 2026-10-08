# TASKS.md — Active and Future Tasks

> Completed tasks (T-01 through T-35, T-11) are archived in [`TASKS_DONE.md`](TASKS_DONE.md).
> **v1.0 is complete.** All items below are post-v1.0 future work.
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

## Current State — v1.1 Complete

- All T-01 through T-35 + T-11, and T-F16/T-F17/T-F18/T-F26–T-F29/T-F37–T-F39 complete and committed
- 95/95 tests pass (`dotnet test`)
- MSIX builds at `src/Archiver.App/AppPackages/` via `Deploy.ps1` (signed with dev cert)
- Satellite EXEs (Archiver.Shell.exe, Archiver.ProgressWindow.exe) included via Content Include in Archiver.App.csproj
- Git tag: `v1.1.0` — GitHub-only release for early testers
- **Store release planned for v1.3** (when shell extension + MOTW + tar.exe complete)

---

## Future Tasks

### T-F02 — Dedicated Archive Window
- [ ] **Status:** future

Separate window for archive configuration instead of inline controls.

---

### T-F05 (original, pre-2026-07-12 scope, superseded by the expanded entry above — kept per the
"never silently deprecate" rule)

Click ZIP in list → read-only tree view of contents via `ZipFile.OpenRead`. No extraction.

---


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

### T-F09 (original, pre-2026-07-12 scope, superseded by the expanded entry above — kept per the
"never silently deprecate" rule)

Expose `Archiver.Core` as standalone CLI executable for scripting, using a GNU-style
`--long-flag` syntax instead of 7z-familiar single-letter commands:

```
archiver archive --src C:\files --dest C:\output --name backup
archiver extract --src C:\backup.zip --dest C:\output
```

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


---


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

## v1.2 — Shell Extension

> **Minimum supported OS:** Windows 10 1809 (10.0.17763.0).
> Shell extension uses dual registration:
> - `desktop4:FileExplorerContextMenus` — Win10 1809+, classic context menu
> - `IExplorerCommand` via COM — Win11 22000+, modern context menu
>
> Both mechanisms invoke `Archiver.Shell.exe`. No separate code paths needed.

---


---

## Context Menu — NanaZip Parity Review (2026-07-04)

Per project direction, NanaZip is the reference implementation for what the Pakko context
menu should offer. Reviewed NanaZip's actual modern (`IExplorerCommand`-based) shell
extension source —
[`NanaZip.UI.Modern/NanaZip.ShellExtension.cpp`](https://github.com/M2Team/NanaZip/blob/main/NanaZip.UI.Modern/NanaZip.ShellExtension.cpp)
— the direct architectural equivalent of `Archiver.ShellExtension`, not the legacy classic
`IContextMenu` implementation (`NanaZip.UI.Classic/.../ContextMenu.cpp`), which is
irrelevant here per this project's `IExplorerCommand`-only constraint.

**NanaZip's full modern-menu command set** (flat list, no separate folder/file/mixed
submenus — conditions are evaluated per-command against the selection, not via distinct
menu trees):

| Command | Condition | Pakko status |
|---|---|---|
| Open | single file, needs extraction | done differently — double-click file association (T-F44); no explicit context-menu verb |
| Test | ≥1 file needs extraction | done — `TestCommand` (see `TASKS_DONE.md`'s T-F62) |
| Extract (dialog, picks destination) | ≥1 file needs extraction | done — `ExtractDialogCommand` (see `TASKS_DONE.md`'s T-F63) |
| Extract Here | ≥1 file needs extraction | done — `ExtractHereCommand` (already smart: `SeparateFolders` mode strips/wraps as needed, equivalent to NanaZip's separate "Extract Here (Smart)") |
| Extract Here (Smart) | ≥1 file needs extraction | n/a — folded into Pakko's "Extract here" above, not a separate verb |
| Extract to "\<folder\>" | ≥1 file needs extraction | done — `ExtractFolderCommand` |
| Compress (dialog, format/options) | any selection | done — `CompressDialogCommand` (see `TASKS_DONE.md`'s T-F63) |
| Compress to "\<name\>.zip" (one click) | any selection | done, but see T-F64 (label says "Add to archive…" though behavior is already the one-click no-dialog path) |
| Compress to "\<name\>.7z" | any selection | out of scope — 7z creation forbidden (`CLAUDE.md`: ZIP only, no third-party compression code) |
| Compress + Email variants (×4) | any selection | **out of scope, deliberately** — mail client integration adds attack surface and a dependency the gov/defense trust model doesn't need; not tracked as a task |
| CRC/Checksum submenu (CRC-32/64, SHA-1/256/384/512, BLAKE2/3, etc.) | any selection | covered by existing T-F46 (File Hash Viewer), which already targets SHA-256; T-F46 is in-app UI only today, not a context-menu verb — cross-referenced, no new task |

**Note on T-F41/T-F42/T-F43:** these three older task entries (below, still `future`/unchecked)
describe "Extract Here", "Extract to Folder", and "Archive with Pakko" as if unimplemented.
They predate T-F61 and are now superseded by it — all three behaviors are implemented and
smoke-tested there. Left in place with a note rather than deleted, per the "never silently
deprecate" rule; do not re-implement them as new work.

---

## v1.3 — tar.exe Integration

---

## v1.4 — GPO + Low IL Sandbox

---

### T-F114 — Performance/Regression Tests vs. a 7-Zip Reference (ZIP only)
- [~] **Status:** implemented and passing 2026-07-17 — all 9 implementation steps done (7za.exe
      vendored, project scaffolded, fixtures/runner built, all 6 scenarios implemented and
      calibrated against real observed ratios, doc cascade complete). Stays `[~]` rather than `[x]`
      for one reason only: the design's own verification criterion ("run on a second,
      differently-specced machine if available, to confirm the ratio actually travels across
      machines") could not be exercised this session — no second machine was available. Everything
      else is done; see `DECISIONS.md`'s T-F114 entry for the observed baseline ratios and full
      rationale.

**What:** automated tests that compress/extract fixture data with Pakko's own ZIP path
(`System.IO.Compression`) and, in the same test invocation on the same machine, run `7za.exe`
against the identical fixture — then assert on the **ratio** between the two elapsed times. Goal:
catch a code change that silently makes compression/extraction meaningfully slower (the project's
actual regression history — accidental sync-over-async, wrong buffer sizes — has been gross, not
subtle), without a flaky absolute-time threshold that breaks the moment the test runs on a
different machine.

**Real-world grounding (research done before designing, not invented scope):**
- Checked whether end users/sysadmins actually track version-over-version archiver speed
  regressions as a demand signal: **thin evidence.** The genre that exists is cross-tool
  comparison ("which archiver is fastest" — 7-Zip's own `7z b` benchmark subcommand, various
  zstd-vs-7z-vs-zip speed/ratio comparison articles), not "this specific tool got slower in its
  last release." Conclusion: this is a sound *internal engineering discipline* to self-impose, not
  something externally demanded — not framed as a user-requested feature.
- Checked how BenchmarkDotNet (.NET's own microbenchmarking library), Rust's `criterion.rs`, and
  Go's `benchstat` solve "compare speed fairly across unknown machines." **None of the three
  attempt true cross-machine baseline portability.** BenchmarkDotNet's `[Benchmark(Baseline =
  true)]` reports every other result as a ratio computed *within the same run on the same
  machine*. `criterion.rs`/`benchstat` compare against a *stored baseline from a prior run on the
  same machine* (not cross-machine). **The only mechanism that generalizes to an arbitrary,
  never-before-seen machine is running the reference and the subject side-by-side, right now, in
  the same invocation, and taking the ratio** — this directly resolves the "what do we compare
  against, given all machines are different speeds" question raised when scoping this task.
- The user's own tentative idea — cache a result in a temp file and compare against it going
  forward — was explicitly considered and **dropped**: that pattern's one legitimate use (per
  `criterion.rs`) is catching drift on the *same machine over time* (e.g. a persistent CI runner),
  and this repo has no CI today (confirmed — manual, occasionally-different-machine, pre-release
  testing cadence per `TESTING.md`). Building that infrastructure now would be speculative; revisit
  only if Pakko ever gets a persistent CI runner.
- 7-Zip's core code is LGPL v2.1+; bundling a portable `7za.exe` for test-only use requires
  attribution (state 7-Zip is used, state LGPL, link to 7-zip.org, include license text) — real,
  small, non-blocking.
- Known pitfalls confirmed from the same research: JIT/cold-start skew (needs one discarded
  warmup pass per engine before timing); process-spawn overhead for the `7za.exe` subprocess
  (fixed per-invocation cost, negligible for large files, noisiest for the many-small-files
  shape — argues for real tolerance headroom, not for dropping that shape); Windows Defender/AV
  interference is real but largely self-cancelling within one ratio (both engines get scanned in
  the same run) — documented as a known flakiness source, not coded around.

**Decisions (confirmed with user, not left as advisor-only recommendations):**
- **Bundle `7za.exe`** (portable, console-only, LGPL) directly in the new test project — pinned
  exact version, SHA-256 verified against the official 7-zip.org release, committed alongside a
  `LICENSE-7-Zip.txt` and a `NOTICE.md` (version/source URL/hash/vendored date). Rejected
  "require a system-installed 7-Zip, skip if absent": `CLAUDE.md`'s own hard constraint is
  "No 7-Zip" for the *shipped product* — the population of machines that build/test Pakko is
  disproportionately likely to not have 7-Zip installed, so "skip if absent" would silently turn
  the gate into decoration on most contributor machines. Explicitly document (code comment near
  the runner + `DECISIONS.md`) that this is a test-only, dev-time dependency, never shipped in the
  MSIX — distinct from the "zero third-party dependencies" rule, which governs `Archiver.Core`'s
  shipped surface only. Absolute-path-only to the bundled binary, mirroring `tar.exe`'s existing
  "never via PATH" convention. Still add a thin `File.Exists` presence guard as defense-in-depth
  (someone deleted the file / `.gitignore` mistake) — documented as an edge case, not the routine
  path `IntegrationAttribute` represents for tar.exe.
- **Drop the cache-in-a-temp-file/persistent-baseline idea entirely** — not built, not stubbed.
  Revisit only if a persistent CI runner is ever added.
- **ZIP only for this task** (`System.IO.Compression` vs. `7za.exe -tzip`), both archive creation
  and extraction. Explicitly **excludes tar-family** — `TarSandboxedService` routes through
  AppContainer/ACL/Job-Object sandbox machinery (T-F52), a deliberate, accepted security cost; a
  shared tolerance band against unsandboxed `7za.exe` would almost certainly "fail" on sandbox
  setup overhead alone, not a real regression. A future tar-family perf task would need its own
  separate calibration accounting for sandbox overhead as a known constant — not bolted onto this
  one.

**Core comparison mechanism:**
- Per (fixture shape × operation): one discarded warmup pass + one timed pass, for both Pakko and
  `7za.exe`, on the identical fixture, in the same test method. Assert
  `r = pakkoElapsed / referenceElapsed <= calibratedBaselineRatio * toleranceMultiplier` — 6
  hardcoded constants total (3 shapes × archive/extract), each derived by running the suite
  locally a few times first and observing real ratios, then picking a multiplier with generous
  headroom (starting point ~2–3x) to absorb machine-to-machine noise. Document the observed
  baseline numbers and chosen multiplier's rationale in `DECISIONS.md` — no bare unexplained magic
  constants.
- Also assert basic sanity (operation succeeded, output entry count/size matches expectation,
  elapsed > 0) alongside the ratio — cheap insurance against a broken timer or short-circuited
  operation silently "passing" on garbage data.
- No repeated-iteration statistics (medians/confidence intervals, criterion/benchstat-style) — a
  single timed run per engine after one warmup is proportionate for a coarse "catch a gross
  slowdown" gate; this repo has no CI to make repeated-run statistics meaningful anyway. Realistic
  sensitivity: catches a ~2x+ slowdown, not a 5–10% regression — matches the actual ask.
- Known residual risk to document, not solve: the ratio assumption holds well for raw clock-speed
  differences between machines, only approximately for *core-count* differences, since `7za.exe`
  is multi-threaded and Pakko's `System.IO.Compression` path is single-threaded — another reason
  the tolerance band needs real headroom rather than a tight one.

**Fixture shapes (generated at test-run time into a `TempDirectory`, matching
`ZipArchiveServiceZip64Tests`' precedent — never committed to git, distinct from
`GenerateFixtures`' small committed correctness fixtures; state this distinction explicitly in
`TESTING.md` so the two mechanisms aren't conflated):**
- **Many small files:** 5,000 files, 1–10 KB each (~25–30 MB total) — deliberately not Zip64's
  65,600 (that count targets the 16-bit entry-count boundary, not perf; at that scale fixture
  creation itself would dominate the perf test's own wall-clock). Kept despite being the noisiest
  shape (process-spawn overhead) because it exercises a genuinely distinct code path (per-entry
  overhead across many small Deflate streams) and was explicitly requested.
- **One large file:** ~300 MB of semi-compressible generated content (a repeating pseudo-text
  pattern — not all-zeros, not pure random noise; either extreme misrepresents realistic
  throughput/ratio behavior), streamed to disk.
- **Hybrid:** ~500 small files (1–50 KB) + 3–5 medium files (5–20 MB), total ~50–80 MB —
  resembles a realistic project-folder archive, kept smaller than the large-file fixture so its
  cost stays proportionate.
- All three hardcoded as `const` fixture parameters directly in the test class, matching
  `ZipArchiveServiceZip64Tests`' `const int fileCount = 65_600` style — no configurable sizing
  system.

**Test project placement:** new dedicated project, `Archiver.Core.PerformanceTests` — not folded
into `Archiver.Core.IntegrationTests`, even though that project already spawns real external
processes (the closest existing precedent). `IntegrationTests` has a settled identity as
"deterministic correctness/security proofs against real OS/tar.exe behavior"; mixing in a timing
assertion with inherent (if small) flake risk blurs that meaning and risks a real regression being
dismissed as "oh, that's the flaky suite." A dedicated project also cleanly owns the bundled
`7za.exe` + its license file rather than attaching an unrelated binary to the sandbox/tar.exe test
project. Every test tagged `[Trait("Category","Slow")]` — picked up automatically by the existing
`dotnet test --filter "Category!=Slow"`/`"Category=Slow"` convention, no new filtering mechanism.
Add to the `.sln` with `Debug|x64`/`Release|x64`-only config entries per `CLAUDE.md`'s hard
constraint (never `Any CPU`/`x86`).

**Category/tagging and failure-handling note (differs from Zip64's Slow tests, document
distinctly in `TESTING.md`):** a Zip64 test failure is always a real bug (deterministic, no
timing) — "just rerun it" is never right there. A perf-test failure carries a nonzero chance of
being a one-off machine hiccup (background scan, thermal throttling, a stray process). Document:
rerun once before treating a perf-test failure as a real regression; a *repeatable* failure across
reruns is the real signal. This repo has no CI — this suite is a manual pre-release gate, same
cadence as the existing Zip64 Slow tests (`TESTING.md`'s Manual Smoke Test Cycle step 6).

**Ordered implementation steps:**
1. Pin an exact 7-Zip release, verify `7za.exe`'s SHA-256, commit it + `LICENSE-7-Zip.txt` +
   `NOTICE.md` (version/URL/hash/date) under `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`.
2. Scaffold `Archiver.Core.PerformanceTests` (net8.0, xunit, FluentAssertions — matching every
   other test project), add to `.sln` with correct x64-only config entries.
3. Shared fixture-generation helper (in a `TempDirectory`, the three shapes as named
   methods/constants).
4. `7za.exe` runner wrapper (absolute path only, `Stopwatch`-timed `Process` invocation for
   `a -tzip` / `x`) plus the thin presence-check safety net.
5. Implement **one** scenario end-to-end first (recommend large-file archive) — get the ratio
   math, warmup handling, and assertion shape right before replicating.
6. Calibrate: run that one scenario locally several times, record observed `r`, pick and hardcode
   the tolerance constant with documented rationale.
7. Replicate across the remaining 5 combinations (2 more shapes × archive+extract, plus extract
   for the first shape).
8. Update docs (see below).
9. Run the full new Slow-tagged suite at least once end-to-end; if a second, differently-specced
   machine is available, run it there too — the whole premise is that the ratio travels across
   machines, worth actually checking once rather than just asserting it.

**Overbuilding — explicitly avoid:** no generic pluggable-benchmark-harness abstraction (e.g. an
`IPerformanceReference` interface anticipating a future WinRAR comparison); no persisted-
history/trend-tracking mechanism (no CI to make it valuable); no statistical rigor beyond one
warmup + one timed run per engine; no configurable fixture-size system; no tar-family coverage in
this task; no auto-download/bootstrap mechanism for the reference binary (commit it like any other
fixture).

**Doc/cascade touches:**
- `TESTING.md` — new section (mirroring the Zip64/Integration structure), update "Running
  Tests"/Manual Smoke Test Cycle step 6, the rerun-once-before-treating-as-regression caveat, and
  the GenerateFixtures-vs-this-suite distinction.
- `CLAUDE.md` — add `tests/Archiver.Core.PerformanceTests/` to Repo Layout; a "Current State"
  entry once implemented; confirm Build Commands' `Category=Slow` line still accurately describes
  what it covers.
- `DECISIONS.md` — new T-F114 entry: why same-run ratio over cross-machine cached baseline (citing
  the BenchmarkDotNet/criterion/benchstat research), why the cache-in-temp-file idea was
  considered and dropped, why `7za.exe` bundled vs. system-installed (with the LGPL attribution
  note), why ZIP-only/tar-family excluded, the calibrated tolerance constants and how they were
  derived, why a dedicated new test project rather than folding into `Archiver.Core.IntegrationTests`.
- `CONVENTIONS.md` — note that `Archiver.Core.PerformanceTests` bundles a test-only, LGPL-licensed
  native binary (`7za.exe`), explicitly distinct from the "zero third-party dependencies" rule
  (shipped product only) — otherwise a future reader hitting `CLAUDE.md`'s "No 7-Zip" hard
  constraint could reasonably be confused finding a 7-Zip binary checked into the repo.
- `tests/Archiver.Core.Tests.GenerateFixtures/README.md` — not modified directly, but the
  distinction from this suite's throwaway perf fixtures should be stated in `TESTING.md`.

**Acceptance criteria:**
- [x] `7za.exe` (x64 + arm64) + `LICENSE-7-Zip.txt` + `NOTICE.md` committed under
      `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`, hash-verified against the official
      GitHub release digest before extraction
- [x] `Archiver.Core.PerformanceTests` project scaffolded, added to `.sln` — mirrors every other
      C# test project's existing Any CPU/x64/x86-all-map-to-Any-CPU config block (not literally
      "x64-only": that reading of the hard constraint doesn't match how any existing C# project in
      this `.sln` is actually configured — see `DECISIONS.md`'s T-F114 entry)
- [x] All 6 scenarios (3 fixture shapes × archive/extract) implemented, each asserting a
      calibrated ratio + basic operation-sanity checks
- [x] Tolerance constants calibrated from real local runs, documented with rationale in
      `DECISIONS.md` (observed ratios 1.06-6.02 depending on scenario, 3x multiplier)
- [x] **Revised same day, user-directed:** the many-small-files/hybrid scenarios (4 tests) tagged
      `[Trait("Category","Slow")]` as originally designed; the one-large-file scenarios (2 tests)
      moved to a new `[Trait("Category","VeryLarge")]` instead — on demand only, never part of the
      normal `Category=Slow` pre-release run. Zip64's own >4 GiB test
      (`ArchiveAndExtract_FileOver4Gb_RoundTripsWithoutError`) moved to `VeryLarge` the same way,
      for consistency (same "genuinely oversized, opt-in only" reasoning). `dotnet test --filter
      "Category!=Slow"` unaffected (confirmed: 43+55+280+55 still pass); `Category=Slow` now runs 4
      perf tests + 3 Zip64 tests (was 6+4); `Category=VeryLarge` runs exactly the 3 gated tests (2
      perf + 1 Zip64), confirmed by name in test output, all passing
- [x] **7za.exe launches sandboxed, user-directed:** every `7za.exe` invocation now runs under a
      basic sandbox reusing `SandboxJobObject`/`SandboxedProcessLauncher` from tar.exe's own T-F52
      subsystem — Job Object only (no child-process creation, 2 GiB RAM / 10 min CPU caps),
      deliberately **without** the AppContainer/quarantine-staging layer (that layer defends
      against untrusted *input*, which doesn't apply to Pakko's own generated fixtures — adding it
      would also risk biasing the very timing being measured via ACL/staging overhead). Mitigates
      the risk of the vendored binary itself being compromised (bounds worst-case resource use,
      blocks spawning further processes) without touching filesystem access. Required adding
      `Archiver.Core.PerformanceTests` to `Archiver.Core.csproj`'s `InternalsVisibleTo` list (same
      mechanism as the two existing entries) since the Sandbox classes are `internal`. Re-ran all 6
      scenarios after the switch — ratios unchanged within normal run-to-run variance (e.g.
      Archive/Hybrid 3.463 vs. the original 3.469-3.519 range), confirming negligible overhead;
      existing calibrated constants did not need adjusting
- [~] Full suite run at least once end-to-end (done, multiple times, confirmed stable both before
      and after the sandboxing change) — a second, differently-specced machine was **not available
      this session** to confirm the ratio actually travels across machines as designed; this
      remains the one open item
- [x] `TESTING.md`, `CLAUDE.md`, `DECISIONS.md`, `CONVENTIONS.md`, `SECURITY.md` updated per the
      cascade above

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

### T-F135 — SonarCloud Static Analysis Integration (CI)
- [x] **Status:** done; closed 2026-10-06 (wave 5 after v1.7.0). The criterion left open below - a real analysed commit on the dashboard - has held for months: every push to `main` is analysed (`api/project_analyses/search` lists revision 9e513f6, 2026-10-06), the quality gate is `OK`, and the open findings are the two documented `S1135` TODO markers (`docs/CONVENTIONS.md`). The rest of the backlog went in the same wave: S4136, S3398 and SYSLIB1092 fixed, S6966 documented as deliberate. C++ stays out of SonarCloud; MSVC `/analyze` covers it (T-F150). **Earlier status:** partial — 2026-07-27. `build.yml`'s `test` job runs JDK setup →
      `dotnet-sonarscanner begin` → the existing `dotnet test` → `dotnet-sonarscanner end`, guarded
      to skip cleanly (not fail) on fork-originated PRs that have no `SONAR_TOKEN` available. Real
      SonarCloud org/project now exist and are confirmed reachable via SonarCloud's public API
      (org key **`pakkoapp-oss-1`**, project key **`pakkoapp-oss-1_pakko`** — note the `-1`, since
      the plain `pakkoapp-oss` key was already taken when the org was created; this does NOT match
      the GitHub org/account name, don't assume it does). `SONAR_TOKEN` repo secret added by the
      user 2026-07-27 (confirmed present via `gh secret list`, value itself not readable/verified
      by the agent). Both keys wired into `build.yml`'s env vars and `README.md`'s quality-gate
      badge; `scripts/README.md` documents the real keys and remaining manual steps.
      **Correction:** the org/project were created via SonarCloud's "Analyze manually" flow (not
      GitHub-App binding), because the importing account lacked Admin on the repo — `pakkoapp-oss`
      is a personal GitHub User account, and personal-account repos don't grant collaborators
      Admin/Maintain roles (same fact already noted elsewhere in this file for T-F124). This means
      PR decoration/auto-binding is not automatically part of this setup; CI-based analysis via the
      scanner still works regardless. **Analysis Method:** high-confidence CI-based already, by
      construction rather than a direct settings check — the user received SonarCloud's
      "Other CI"/manual `dotnet-sonarscanner begin`/`end` setup snippet, which SonarCloud only
      surfaces for CI-based projects (Automatic Analysis needs the GitHub App installed with repo
      Admin, which was never available here — see the "Analyze manually" correction above; that
      path structurally can't end up in Automatic mode). Not a substitute for eyeballing
      Administration → Analysis Method directly, but strong enough not to block on. **Still open
      before this graduates to `[x]`:** one real CI run (a push, since PR runs against `main` need
      an actual PR) completing successfully, then checking the result against the live SonarCloud
      dashboard, not just a green Actions run. C++ (`Archiver.ShellExtension`) stays deliberately
      out of scope for this pass. This project's own rule for CI changes (T-F122/T-F125
      precedent): don't graduate on YAML/API-reachability alone — a real analyzed commit has to
      show up on the dashboard.
      **Coverage follow-up, same day:** the org's default "Sonar way" quality gate requires new
      code coverage ≥ 80% — with no coverage submitted, that condition reports "no data" and fails
      the gate regardless of everything else. `dotnet test` now runs with
      `--collect:"XPlat Code Coverage"` (via the `coverlet.collector` package every test project
      already references — no new dependency) and `dotnet-sonarscanner begin` picks up the
      resulting reports via `/d:sonar.cs.cobertura.reportsPaths="**/coverage.cobertura.xml"`. This
      feeds a real number into the gate; it does not guarantee that number clears 80% — a real gate
      failure from insufficient coverage on new code is a legitimate signal, not a setup bug.
      **Branch-naming bug found and fixed, same day:** SonarCloud's manual project creation
      defaulted the designated "Main Branch" to the literal name `master`, which this repo has
      never had (only `main`) — every real analysis was landing on a short-lived, non-main branch
      record instead, so `qualitygates/project_status` returned `NONE` and the README badge would
      have shown a stale/unanalyzed state indefinitely. Fixed via SonarCloud's Administration →
      Branches → Rename Branch (`master` → `main`); confirmed via `api/project_branches/list`
      that `main` is now `isMain: true`, `type: LONG`. A second real CI run (empty commit
      `9144efa`, pushed specifically to re-trigger analysis against the corrected branch) confirmed
      via `api/ce/component` (status `SUCCESS`, `branch: main`, `branchType: LONG`) and
      `api/measures/component` real project-wide numbers: 16 bugs, 8 vulnerabilities, 245 code
      smells, 74.8% coverage, 2.1% duplication, reliability E, security C, maintainability A.
      **Quality Gate itself still reads `NONE` — expected, not a bug:** all 6 "Sonar way"
      conditions evaluate against *New Code* (delta from a previous analysis), and this was the
      first-ever analysis of the correctly-named branch, so there's no prior baseline to diff
      against yet. It becomes evaluable starting with the next analysis.
      **Still open before `[x]`:** (1) a third analysis, so the Quality Gate actually evaluates to
      a real PASSED/FAILED instead of `NONE`; (2) the "existing findings triaged" acceptance
      criterion below — 16 bugs / 8 vulnerabilities / 245 code smells currently sit unreviewed,
      real remaining work, not a CI-wiring gap.
- **Depends on:** T-F122 (`build.yml` CI workflow, done)

**What:** wire SonarCloud into `.github/workflows/build.yml` so every push/PR gets a real static-
analysis pass (bugs, vulnerabilities, code smells, duplication) across the .NET projects, not just
`dotnet test` green. Must stay **free** — SonarCloud's free tier only applies to genuinely public
repositories, which this repo is (confirmed: `origin` is `github.com/pakkoapp-oss/pakko`, public).
Directly continues this project's own hard constraint: *"A CI-run static analyzer's findings on
your own PR/commit get fixed in the same pass ... tests prove behavior, an analyzer proves
robustness to future change"* — today that rule has no analyzer feeding it in CI at all.

**Acceptance criteria:**
- [ ] Confirm the project qualifies for SonarCloud's free plan (public GitHub repo, no private
      forks in scope) before doing any setup work — don't assume, check their current published
      terms at the time this is picked up
- [ ] SonarCloud project created and linked to `pakkoapp-oss/pakko` (organization + project key)
- [ ] `SONAR_TOKEN` added as a GitHub Actions secret (repo secret, never committed to a tracked
      file — per this project's public-repo-hygiene rule)
- [ ] New step(s) in `build.yml`'s `test` job (or a new dedicated job) run the SonarCloud C#
      scanner (`dotnet-sonarscanner begin`/`end`) wrapping the existing `dotnet build`/`dotnet
      test` invocation, so coverage/analysis data comes from a real build+test run, not a
      standalone lint pass
- [ ] Scan scope covers the .NET projects (`Archiver.Core`, `Archiver.App`, `Archiver.App.Core`,
      `Archiver.Shell`, `Archiver.CLI`) — decide during implementation whether the C++
      `Archiver.ShellExtension` project is in scope for this same pass or deferred (SonarCloud's
      C++ analysis has separate licensing/setup considerations; don't assume it's free-tier
      covered without checking)
- [ ] A real CI run (not just a local read of the YAML) completes and a real SonarCloud project
      dashboard shows results — this project's own rule for CI changes (T-F122/T-F125 precedent):
      graduate only after a real workflow run + a real check against the actual SonarCloud UI, not
      on green YAML syntax alone
- [ ] `README.md` gets a SonarCloud quality-gate badge (standard practice for public OSS repos
      using it) — small, teaser-only, per this project's canonical-topic-owner rule (no duplicated
      detail, just the badge/link)
- [ ] Any findings SonarCloud reports against **existing** code at first-scan time are triaged (not
      silently ignored) — real bugs/vulnerabilities fixed, accepted false positives suppressed with
      a reason via SonarCloud's own suppression mechanism, not a blanket `// NOSONAR` sweep
- [ ] `docs/TASKS.md`/`CLAUDE.md` updated once this ships, so the existing "CI-run static analyzer"
      hard constraint has a real analyzer to point at

---

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

### T-F226 — Architecture review of the whole implementation against our rules (next research step)

- [~] **Status:** 2026-09-24 — findings filed as T-F227..T-F244 (plus additions to T-F201 and
  T-F232); batch 2 (2026-09-25, the sandbox files, AMSI and format detection the first pass did
  not reach) filed T-F245..T-F249 plus additions to T-F233 and T-F244. Batch 3 (2026-09-25:
  `GroupPolicyService`, preview/nested caches, `FileHashService`, Shell password and conflict
  dialogs, `Localization.cpp`, `SECURITY.md` against code, check K, check L spot-check) filed
  T-F250..T-F259; `scripts/*.ps1` were pattern-searched only (non-ASCII literals, BOMs, recursive
  deletes). Check K ran: 7 mutants over the security gates, 6 killed by the right tests, 1
  survivor (T-F256). **Not closed:** check L was a spot-check of diagrams 1, 2, 4, 7 (T-F258);
  diagram 6 is unchecked and the per-arrow ground-truth ritual over `docs/DIAGRAMS.md` is still
  deferred until the fixes for T-F227/T-F228/T-F233/T-F236 rewrite those diagrams (with T-F165/
  T-F223) — the review's exit criterion is not met for that cell. **2026-09-29 (G5):** the ritual
  ran for diagrams 1, 2, 3 (password gate), 4 and 7 (T-F258) and the new diagram 9 (T-F223);
  T-F165 was already done 2026-08-12. Diagram 6 is still unchecked — now T-F292. **2026-10-03:**
  T-F292 redrew diagram 6 per the ritual. The cell is still not closed: diagrams 5 and 8, and
  diagram 3 beyond the password gate, have not had the per-arrow ritual since the fixes.
  Checked with no finding: the fire-and-forget calls and `async void` (event handlers only),
  `static` mutable state (`FileHashService._threadPoolWarmed` is a process-wide one-shot latch around
  a process-wide setting), `ArchiveTreeIndex` recursion (iterative; memory issue is T-F237),
  `QuotePath` quoting, Authenticode verification, Job Object/attribute-list lifetimes.
  Performance (one run of `Category=Slow`, all 10 pass within the 3x tolerance): Archive/
  ManySmallFiles 1.61, Archive/Hybrid 2.47, Extract/ManySmallFiles 2.08, Extract/Hybrid 2.33,
  Hash 0.98 (Pakko/7za). The archive ratios are well above T-F35's recorded ~1.0/~1.3 — rerun
  on an idle machine before calling it a regression.
  Original plan text follows. Discovery only, like T-F202. A senior-architect
  review of all projects against the written rules (global `CLAUDE.md` Code Behavior,
  `~/.claude/dev-practices.md` sections 2-5, 7, 8, `cross-language-style.md`, this repo's Hard
  Constraints/Do Not, `docs/CONVENTIONS.md`, `SECURITY.md`, `docs/ARCHITECTURE.md`,
  `docs/DIAGRAMS.md`). Checks: resources, bounds provable from the line, immutability/static state,
  recursion over untrusted input, the "never throw" error contract, concurrency and UI marshaling,
  the three safety legs, architecture boundaries and duplicated decision logic (a four-frontend
  parity matrix), test categories and isolation level, docs/diagrams ground truth, CI tier (fuzzing,
  sanitizers, dependency audit), the `pakko://`/file-association activation surface as untrusted
  input, and code-page handling at every process/console boundary (the test machine runs ANSI
  65001, which hides code-page bugs). First pass: sweep for siblings of the bug classes T-F202 found
  (T-F204, T-F205, T-F207, T-F211). Done = every component x check cell is checked with
  `file:line`, filed as a finding (T-F227 onward), or N/A with a reason. Full method: the batch plan's
  section 6.
- **Reported by:** user request, 2026-09-24.

### T-F254 — Explorer menu stays English for Chinese and regional-variant Windows languages (P2)

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

### T-F261 — Routing and Group Policy have no single owner: Test/Scan bypass the routers, the policy is optional and fail-open (P1, root, decision)

- [~] **Progress (2026-09-28, fix phase 5, wave 2 track A):** code complete (6d5ee6a).
  `ArchiveFormatPolicy` is the one public classifier (policy checked before tar capability);
  `IExtractionRouter.TestAsync` (Shell `--test` and CLI `t` use it, tar.exe never starts for Test);
  `GroupPolicyOptions` is required on every engine and router (reflection test);
  `TarSandboxedService` refuses Extract/List/Compress and skips the version probe under
  `DisableTarExtraction`; `PakkoServices.Create(policy)` builds Shell and CLI services (CLI `i`/`l`
  had no policy at all before). The App keeps DI; the engine gates cover it. Device check with real
  policy values pending.
- **Device check (2026-09-28, Deploy 1.5.0.15, policy set through an elevated helper, tar.exe
  processes polled every 100 ms):** `DisableTarExtraction=1`: `pakko i` shows every tar format
  "blocked by Group Policy" and "tar.exe ... (disabled by Group Policy)"; `pakko l`/`t`/`x` on
  `.tar.gz` and `l` on `.7z` refuse with the policy reason (zip still lists); App start + browse
  `.7z` shows the policy error; Shell extract/test/scan on `.tar.gz` refuse; no tar.exe seen in any
  of ~10 operations (control run with the policy removed: the poll caught 2 of 5 version probes plus
  a listing). `BlockedFormats=sevenzip`: `pakko l x.7z` refuses, `.tar.gz` still lists.
  `BlockedFormats=zip`: Shell test/extract/scan on a zip refuse. The Explorer menu part is under
  T-F262. Found: T-F274 (Test says "no errors" when nothing was tested); the App's error box is
  English ("Error" + Core reason) in the uk-UA UI, T-F209.

- [x] **Status:** done; status line corrected 2026-10-06. The device check with real policy values is the 2026-09-28 entry above, and it was repeated on the v1.7.0 release candidate 2026-10-04 (`DisableTarExtraction=1` through an elevated `reg add`: `pakko i`/`l`/`x`/`a` and Shell extract refuse with the policy reason, ZIP still works, no tar.exe started, the Explorer menu drops the tar item; policy removed after). **Earlier status:** fixed in code (see Progress above); device check pending (G6/G7). Status line synced 2026-09-30. Original: open — code-confirmed 2026-09-25. Extract, Create and List have routers; Test
  has none (`IExtractionRouter.cs:10-17` has only `ExtractAsync`, although
  `docs/ARCHITECTURE.md:73` says it routes `TestAsync`): Shell sends every path to the ZIP engine
  (`Archiver.Shell/Program.cs:292-299`, the root of T-F216), the CLI re-runs format detection
  with its own reason text (`Archiver.CLI/Program.cs:288-298`), the App has no Test (T-F241).
  Every router and engine takes `GroupPolicyOptions?` defaulting to "allow everything"
  (`ExtractionRouter.cs:11-13`, `ArchiveCreationRouter.cs:9-12`), so a call site that forgets it
  fails open — the CLI still does at `Program.cs:324,438-440`. `ArchiveListingRouter` takes no
  policy and keeps its own copy of `IsSupported`/`BuildUnsupportedReason`
  (`ArchiveListingRouter.cs:33-56`), justified by "two call sites", which stopped being true when
  T-F146 added `ArchiveFormatPolicy`. The CLI `i` command has a third, hand-written format table
  (`Program.cs:327-337`). "Never spawns tar.exe" (`docs/POLICIES.md:44`) is enforced by callers,
  not where tar.exe is launched. Composition roots are hand-built per command in Shell
  (`Program.cs:211-216,265,292,576-581`) and CLI (`Program.cs:77-79,301,324,367,438-440`) and
  each decides again whether to pass the policy — the same drift T-F51's plan hit when it missed
  the CLI.
  (Checked and refuted: Scan does not bypass the policy — `AntivirusScanService.cs:80` runs
  `ArchiveFormatPolicy.Classify` before it opens a `TarSandboxScope` directly.)
- **Fix seam:** `ArchiveFormatPolicy` as the single, public classifier for every operation;
  `TestAsync` on the router; `GroupPolicyOptions` required, not optional; the
  `DisableTarExtraction` gate also at the tar launch point (`TarSandboxedService`/
  `TarSandboxScope`) and before the capability probe; one plain Core factory function (e.g.
  `PakkoServices.CreateAsync(GroupPolicyOptions)`, not a DI container) that Shell and CLI use —
  justified by the two real misses above, not speculative.
- **Decision for the user (fix-batch index item 0):** `docs/ARCHITECTURE.md:1485` records that
  listing is *deliberately* not policy-gated, citing `ITarService.ListEntriesAsync`'s doc comment
  — but that comment (`ITarService.cs:35-41`) is about the entry-safety pre-scan, not Group
  Policy, while `docs/POLICIES.md:44` promises tar.exe is never spawned. Pick one: gate listing
  (T-F250 as filed) or narrow POLICIES.md's promise.
- **Leaves:** T-F250, T-F216, T-F241, T-F262.
- **Tests first:** a fake `ITarService` that fails the test when called, for each operation
  (extract, create, list, test, scan) under `BlockedFormats` and `DisableTarExtraction=1`; the
  factory passes the loaded policy to every service it builds.
- **Reported by:** architecture review, 2026-09-25.
- **Decision (2026-09-25):** gate listing by Group Policy; correct `docs/ARCHITECTURE.md:1485`.

### T-F285 — tar creation from a `subst` drive root fails inside tar.exe (P3)

- [x] **Status:** done 2026-10-06 (wave 5 after v1.7.0). Wider than first filed: tar.exe (bsdtar 3.8.8) cannot visit a drive root at all - real volume (`tar -cf o.tar -n C:\`), mapped network drive and `subst` alike - and on a `subst` drive it also fails for a file directly in the root. Fix: a root source is listed as one `-C root` + name pair per top-level entry, and a `subst` drive is replaced by its folder (`SubstDrive`). Tests first (`TarSandboxedServiceDriveRootTests`, a `subst` drive over a temp folder; both failed with "GetVolumePathName failed: 123"). Device (Release `pakko.exe`, `pakko a -ttar`): `subst` root -> `r.txt, sub/, sub/s.txt`; file in the `subst` root; folder on it; mapped network drive root (`net use` to a localhost share) -> the same three entries; UNC folder and plain folder unchanged (wrapped in the folder's name). Closing review added what a real root holds (tests first, three red): entries both Hidden and System are left out (`tar -C C:\ "System Volume Information"` exits 1, so every NTFS drive would fail), a junction in the root is skipped with the message a selected link gets, and with the archive written into the root (`pakko a -ttar P:\out.tar P:\`) neither it nor its temporary file is packed. Shell `--archive --format tar P:\` wrote `P.tar` to the Desktop (a root has no parent; T-F99's rule) with `r.txt, sub/, sub/s.txt` and no `pagefile.sys`. Not run: a whole real volume root. Decision: `docs/DECISIONS.md`, waves 4 and 5. **Earlier status:** open — found 2026-09-29 in the T-F283 device pass. Shell `--archive --format tar
  P:\` (P: = `subst` of a small folder) shows "tar.exe не зміг створити архів: ... Couldn't visit
  directory" with a garbled path. `tar.exe` alone fails the same way with `P:\` as an argument
  (the pre-T-F283 form), as a `-T -` list line, as `P:/`, and as `-C P:\` + `.`; `-C P:\` +
  `r.txt` fails with "GetVolumePathName failed: 123". A bsdtar limitation, not a Pakko regression.
  A real volume root was not tried (needs a small real drive or an elevated VHD).
- **Fix direction:** check a real volume root first; if only `subst`/mapped drives fail, resolve
  the drive to its target path (`QueryDosDevice`) before building the list, or refuse with a clear
  message. The archive name for a drive root is T-F281.
- **Reported by:** T-F283 device pass, 2026-09-29.

### T-F275 — Recovery data for archives: PAR2 files next to the archive (P3)

- [ ] **Status:** open — scheduled by the user 2026-10-05 as a wave of its own, after the
  post-v1.7.0 fix waves (T-F333's tail and T-F280, the `pakko` messages, the App window items,
  the localization and listing tails, the infrastructure items). It starts with the
  `docs/SPEC.md` scope entry and the two open questions below; then write-side (create +
  par2cmdline accepts it), then verify, then repair, each shippable alone.
  Option to write PAR2 (Reed-Solomon) recovery files next to a created archive, with a chosen
  redundancy (e.g. 5%), and to verify/repair an archive from them. Use: archives kept on flash
  drives or optical media or carried offline, where bad sectors or a truncated copy are the
  realistic damage.
- **Decision (user, 2026-09-28):** PAR2 files next to the archive, not a recovery entry inside the
  ZIP. Reasons: third-party tools (par2cmdline, MultiPar) can check and repair them, which fits the
  auditability goal; no Pakko-only format to maintain; a recovery entry inside the ZIP would be
  lost with a truncated tail (the central directory is at the end) unless written first, and
  other archivers would extract it as an unexplained file.
- **Constraints:**
  - Recovery is computed over the finished archive's bytes — for an encrypted ZIP that is
    ciphertext, so the PAR2 files reveal nothing beyond it and are not encrypted themselves
    (repair must not need the password). Never compute it over plaintext.
  - Output must be standard PAR2 that par2cmdline accepts for verify and repair (tested against
    it, like the 7za cross-checks). Reed-Solomon is Pakko's own code: no NuGet packages in Core.
  - Reading PAR2 files is new untrusted input: bounded memory and sizes, every field validated,
    repair writes only to a new file next to the archive, never over the original.
  - Tests: flip bytes, zero whole blocks, truncate the tail, damage the PAR2 files themselves;
    the repaired archive must match byte for byte, and damage beyond the redundancy must fail
    cleanly.
- **Scope (user, 2026-09-28):** every archive format Pakko creates (ZIP and the tar family — the
  scheme works on the archive's bytes, so the format does not matter). Offered wherever the user
  sets archive options: the App's "New archive" card, `pakko a`, and Explorer's "Compress..."
  options dialog; not on Explorer's one-click verbs ("Add to X.zip"/"Add to X.tar"), which have no
  options.
- **Open questions:** where verify/repair lives (App, `pakko`, an Explorer verb on a `.par2` or
  the archive); Group Policy control.
- **Reported by:** user question, 2026-09-28.

### T-F290 — tar.exe on Windows ARM64 crashes on a non-ASCII name argument (P3)

- [ ] **Status:** open. On the `windows-11-arm` runner, `C:\Windows\System32\tar.exe -czf
  out.tar.gz <name>` with a Cyrillic file name exits with 0xC0000005 (access violation), both
  attempts of the T-F240 canary run 36616898216. The x64 runners pass the same fixture. The failing
  call is the test fixture (`ExternalTarFixtureBuilder`), not Pakko. Open question, which sets the
  priority: does it crash only on a name the ANSI code page cannot represent (Pakko already refuses
  those before tar.exe runs, `TarCommandLineEncoding.IsRepresentable`), or on any non-ASCII name
  (then TAR creation on an ARM64 machine whose code page does cover it, e.g. Cyrillic under 1251,
  would hit it)? `canary-arm64`'s "tar.exe name probe" step logs the tar version, code pages and
  exit codes per name and form; read it, then decide. Until then
  `ExtractAsync_TarGzWithUnicodeFilenameAndContent_ExtractsCorrectly` is skipped on an ARM64 OS
  (`IntegrationNotOnArm64Attribute`).
- **Probe result (run 36620565447, bsdtar 3.8.8, ACP 1252, OEM 437):** `plain.txt` and `café.txt`
  exit 0 with `-cf` and `-czf`; a Cyrillic name exits 0xC0000005 with both. So it crashes on a name
  the ANSI code page cannot represent, not on any non-ASCII name — Pakko refuses such names before
  tar.exe runs, so production is not exposed; P3. Left open: the same check on a machine whose ACP
  covers Cyrillic (1251), and whether to report it upstream. (The probe's list-file runs are not
  evidence: pwsh wrote the list in UTF-8, which tar.exe reads in the ACP.)
- **Reported by:** T-F240 canary, 2026-09-29.

### T-F311 — `Archiver.App.Core.Tests` fail once in a while under a full-suite run (P3)

- [x] **Status:** done 2026-10-06 (wave 5 after v1.7.0). `FileItemTests`: cause found - `FileItem` sets `Crc32` and then `Crc32Display`, and the test waited on the first and read the second; a probe that read the display the moment `Crc32` appeared saw the old text in 2,989 of 3,000 tries. `FileItem.Crc32Ready` (a task, like `TotalsReady`) completes after both, and the tests await it instead of polling for 10 s. `SourceRecyclerTests`: the failure was not reproduced; the test read every `$I` record of the user's real Recycle Bin and failed if one belonging to another process vanished or was held between the listing and the read - such a record is now skipped. If it fails again, the message will show which assertion. **Earlier status:** open. Seen 2026-09-30 in wave 1, in different full `dotnet test` runs:
  `SourceRecyclerTests.Win32_MoveToRecycleBin_FileLandsInRecycleBinNotDeleted` and
  `FileItemTests.TryCreate_ExistingFile_ReturnsItemWithSizeAndCrc`, each once; both pass alone and
  in three reruns of the project. Code the wave did not touch. Find the shared resource or timing
  (the real Recycle Bin; an async CRC load) and make the tests deterministic, as T-F162 did for
  `Progress<T>`.
- **Reported by:** v1.7.0 wave 1 (agent), 2026-09-30.

### T-F314 — App: focus lands on the Up button after Enter opens a nested archive (P3)

- [x] **Status:** done 2026-10-06 (post-v1.7.0 wave 3, 070c649). A focus log on the device showed the cause: the queued focus call ran 6 ms after the open, before the list's layout pass (`no container, items=2`), so `Focus` was never called, and 160 ms later the old row went away and focus fell to the Up button. `FocusRowLater` now forces the layout pass first. Device (dev 1.7.1.1): Enter on `inner.zip` puts focus on the first row, the next Backspace pops the level and focuses a row, Backspace again reaches the real folder. **Earlier status:** open. Split out of T-F308 (user, 2026-10-03). In the Archive Browser, Enter on a
  nested archive opens it, but keyboard focus ends on the browse Up button instead of the first row,
  so the next Backspace/Alt+Up does nothing until the list is focused again (Enter on a folder keeps
  focus on a row). Three fixes in `MainWindow.xaml.cs` did not hold on the device (a deferred `Focus`
  at Low priority, bounded retries, a live-container check) — see `docs/DECISIONS.md`'s T-F308 entry.
  Next idea, not tried: handle Backspace/Alt+Up for the whole browse area (the Up button included),
  or find what moves focus to the Up button while the nested level loads (the list is disabled while
  busy).
- **Reported by:** v1.7.0 wave 4 device pass, 2026-10-03.

### T-F315 — Operation window sometimes renders all black until activated (P3)

- [x] **Status:** closed 2026-10-06 (post-v1.7.0 wave 3) as not reproducible. Dev 1.7.1.1, `--test <corrupt.zip>` started in the package identity (`Invoke-CommandInDesktopPackage`; a direct `Start-Process` of the installed Shell no longer runs): 11 of 11 windows never had the foreground and were fully drawn (10 in the light theme by pixel sampling of the screen region, 1 in the dark theme by a region capture), raised with `SWP_NOACTIVATE` only so that the region could be read. With the 4 of 4 on 1.5.0.34 that is 15 launches without the fault since 1.5.0.13. The launcher differs from the one that reproduced it, so this is weaker than a same-launcher run; reopen if a black window is seen again. **Earlier status:** open. Split out of T-F268 step 6 item (f) on its closing, 2026-10-03. The window fades in black (caption buttons only) when it never gets the foreground; one real activation fixes it for good. Reproduced 16 of 16 on 1.5.0.12/13 (`Start-Process` from a background shell), not reproduced 4 of 4 on 1.5.0.34; the real Explorer flow passes the foreground right and was never seen black. Full trail, the tries that failed and the next ideas: T-F268's (f) note. Reproduce before fixing.
- **Reported by:** T-F268 Explorer smoke (agent and user), 2026-09-27.

## Test-Coverage Audit Follow-Ups (T-F174–T-F186)

Sourced from a full three-stage QA/AppSec coverage audit (requirements extraction -> matrix vs.
existing tests across Happy/Error/Misuse/Security/Boundary vectors -> gap analysis), 2026-08-30.
Two gaps the audit surfaced were already tracked (T-F160 CLI conflict-dialog parity, T-F171
Tar-family duplicate-entry-name parity) — not duplicated here, only cross-referenced. Priority
tiers below (P0/P1/P2) reflect the audit's own ranking for a standalone security-sensitive desktop
app, not strict execution order — `docs/DECISIONS.md` gets an entry once each task's real findings
land, per this project's normal workflow.

## ZIP Password Support (T-F188–T-F194)

Sourced from a dedicated Plan Mode + `advisor` design session, 2026-09-17 (user-directed:
"Подготуйся до впровадження читання зіп з паролями" — prepare password-protected ZIP reading,
covering Explorer/WinUI App/Archive Browser/CLI password-prompt UX and a test-first strategy,
before any code). **This reverses `SPEC.md`/`SECURITY.md`'s "Encrypted archives — Out of scope"
line** — explicitly confirmed with the user as a deliberate scope change, not an oversight. Those
documents (`SPEC.md`, `SECURITY.md`, `docs/CLI.md`, `README.md`, `docs/index.html`+`uk/`) are
updated when each task below actually ships, not in advance — this project's own established
timing convention (e.g. T-F105 only flipped `SPEC.md`'s TAR row once TAR-creation actually worked).

**Confirmed scope (user-directed):**
- **ZIP only.** `tar.exe` (bsdtar 3.8.8/libarchive 3.8.8, `C:\Windows\System32\tar.exe`) has zero
  passphrase support — confirmed empirically via `tar --help` (no password/passphrase mention at
  all) this same session. RAR/7z stay diagnostics-only, unchanged (T-F113).
- **Reading only, this phase.** Decrypts both legacy ZipCrypto (PKWARE traditional) and WinZip
  AE-1/AE-2 (AES-128/256), for maximum compatibility with real-world files. Writing (creating
  password-protected archives) is a separate future phase — AES-only, never ZipCrypto — tracked as
  T-F193 below, not implemented alongside T-F188–T-F192.
- **One shared resolver for both directions from day one** (T-F157→T-F158 precedent — that pair
  had to retroactively unify a decision that was first built extraction-only; this is deliberately
  avoided here): `PasswordResolver` and the `ResolvePasswordAsync` callback shape on
  `ExtractOptions`/`ArchiveOptions` are designed for both `Purpose: Decrypt|Encrypt` from T-F189
  onward, even though only `Decrypt` has a real caller until T-F193.

Full design rationale — the AE-1/AE-2 CRC-zeroing pitfall, the no-fork-extraction-pipeline
invariant, the raw-entry-locator-vs-reflection choice, and the empirical `tar.exe`/`7za.exe`
findings — gets its own `docs/DECISIONS.md` entry once T-F188 actually lands; the full design plan
(reviewed twice by `advisor`) is preserved in this session's plan file until then.

---

### T-F317 — `pakko` in the terminal for Store and winget users (P2, user-requested)

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

### T-F319 — A real archive that fails to list from a browsed folder drops the browser (P3, UX)

- [x] **Status:** done 2026-10-06 (post-v1.7.0 wave 3, a9b3bfb). `EnterBrowseModeAsync` reads the scope from before the open and `BrowseNavigation.DecideListFailure` (App.Core, `BrowseNavigationTests`, two mutants killed) picks the exit: back to the real folder, or the pending list for every other way in. The destination folder is set only after the archive lists. Diagram 6 updated. Device (dev 1.7.1.1): Enter on a corrupt `.zip` in `C:\tmp\w3` shows the error dialog over the same folder, and focus returns to the row when it closes. Only the `Success == false` exit was run on the device; when the listing throws, its own error dialog shows before the folder is restored (Core does not throw there). **Earlier status:** open. In the Archive Browser outside an archive (T-F107), double-clicking a real
  archive runs `EnterBrowseModeAsync`. When that listing fails (an error dialog, or `!Success`), it
  sets `IsBrowsingArchive=false`. The user lands in the pending list, not back in the folder they
  were browsing, and the folder location is lost. Expected: stay in `RealFolder` after the error
  dialog. Fix in the view model, with the decision extracted to App.Core so it is testable. Update
  diagram 6's exit arrow in the same commit. Check on device with a corrupt `.zip` in a browsed
  folder.
- **Reported by:** T-F292 redraw of diagram 6, 2026-10-03.

### T-F320 — MSIX write virtualization hides new top-level AppData folders (P3)

- [ ] **Status:** open. Found while checking T-F317 on device (2026-10-03). A packaged process
  (the `pakko` alias, and so also App and Shell) that creates a **new folder directly under**
  `%LOCALAPPDATA%` or `%APPDATA%` gets it redirected to
  `%LOCALAPPDATA%\Packages\PavloRybchenko.Pakko_<hash>\LocalCache\...`; nothing unpackaged sees it.
  Measured: `pakko x t.zip -o%LOCALAPPDATA%\New` → files only in `LocalCache\Local\New`; the same
  into `%APPDATA%\New` → not visible; into an existing `%LOCALAPPDATA%` folder or a new subfolder
  of one → real location; `%TEMP%` → real location; Shell `--extract-here` next to an archive in an
  existing `%LOCALAPPDATA%` folder → real location. So only the "new top-level AppData folder"
  shape is affected — rare from Explorer, more reachable from scripts.
- **Fix to evaluate:** what NanaZip ships — `rescap:Capability Name="unvirtualizedResources"` plus
  `desktop6:FileSystemWriteVirtualization` disabled. A restricted capability needs a justification
  in the Store submission, so decide it with the next Store upload (v1.7.0 wave 9). Until then
  documented in `docs/CLI.md` (Distribution).
- **Decision (2026-10-03, wave 9, agent):** not fixed for v1.7.0 — the restricted capability can
  delay Store certification and the shape is rare with a workaround; a known issue of the release
  (`docs/DECISIONS.md`'s wave 9 entry). Revisit with a Store upload that has time for a review round.
- **Reported by:** T-F317 device check, 2026-10-03.

### T-F324 — Archive Browser lists hidden system folders at a drive root (P3, UX)

- [x] **Status:** done 2026-10-06 (post-v1.7.0 wave 3, 1fff1c1). Decision (agent): always leave out entries that are both Hidden and System, files and folders — what Explorer hides as protected operating system files, whatever its "show hidden items" setting. Hidden-only entries stay: Up from an archive under `%TEMP%` climbs through `AppData`, and the folder just left must be in its parent's list. Device (dev 1.7.1.1): `C:\` lists no `$Recycle.Bin`, `Config.Msi`, `Documents and Settings`, `Recovery`, `System Volume Information`, `pagefile.sys`; `ProgramData` is listed. **Earlier status:** open. Found in the wave 9 device campaign (2026-10-03): browsing up to `C:\`
  lists `$Recycle.Bin`, `Config.Msi`, `Documents and Settings` (a junction) and other hidden or
  system entries that Explorer hides by default. Decide: follow Explorer's "show hidden items"
  setting, or always hide Hidden+System entries in `FileSystemBrowser` (T-F107).
- **Reported by:** v1.7.0 wave 9 device campaign, 2026-10-03.

### T-F326 — Tar-creation policy refusal names the destination folder as the item (P3)

- [~] **Status:** fixed in code 2026-10-05 (post-v1.7.0 wave 2, d42a676), by tests: every whole-operation refusal (Group Policy, a password with a tar format, an unusable password, a failed tar.exe signature check) names the archive that was to be written (`ArchiveNaming.RefusedArchivePath`), or the first source in Separate-archives mode. Left: one `pakko a -ttar` run under `DisableTarExtraction=1` on a device (needs UAC; goes with the pre-release visit).
- **Earlier status:** open. Found in the wave 9 device campaign (2026-10-04, B9) with
  `DisableTarExtraction=1`: a `pakko a -ttar` run with its output in `C:\g9` prints
  `pakko: error: g9: tar.exe-based archive creation is disabled by Group Policy.`. The refusal is
  about the format, not about a file or folder, but `ArchiveCreationRouter` and
  `TarSandboxedService.CompressAsync` both build it as `CoreMessages.Error(options.DestinationFolder,
  TarCreationDisabled)`, so the folder reads like the item that failed. Fix direction: name the
  archive being created (or no item at all) in both places; check how App and Explorer show the
  same error.
- **Reported by:** v1.7.0 wave 9 device campaign, 2026-10-04.

### T-F327 — Switching the Windows app theme while the App is open leaves the window background in the old theme (P3)

- [x] **Status:** done 2026-10-06 (post-v1.7.0 wave 3, b8ddab6). Cause: `RootGrid.Background` was read once in code from `Application.Current.Resources`, which gives the start theme's brush; it is a `ThemeResource` in XAML now. Device (dev 1.7.1.1): dark to light and back with the App open in the browser — dark to light seen on the whole window, light to dark on the part not covered by another window. The operation window's result view (Mica background, default-styled text) followed light to dark; its code-assigned brushes and its non-Mica background are T-F340. **Earlier status:** open. Found while taking the v1.7.0 screenshots (2026-10-04, CI build of 039955a):
  with the App open in the dark theme, setting Windows to the light app theme turned the cards,
  buttons and text light, but the window background and the list area stayed dark (dark text on a
  dark background in the list). A fresh start in either theme is correct. Check what paints the
  window background (`RootGrid`, the backdrop, the title bar colours set in `MainWindow.xaml.cs`)
  and whether it follows `ActualThemeChanged`; check the Explorer operation window too.
- **Reported by:** v1.7.0 screenshot session, 2026-10-04.

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

### T-F334 — Two checks from the v1.7.0 release that only a person can do (P3)

- [x] **Status:** done 2026-10-06, checked by the user on dev 1.7.1.1 built from 42bd2f6 (installed 08:01, binaries compared by hash with the build): dragging from Explorer onto the App's list works, and the tray menu's Exit works. This is the check before v1.7.2. **Earlier status:** open. Left unchecked at the v1.7.0 tag and again in the smoke pass of the
  release bundle (2026-10-05): dragging files from Explorer onto the App's file list (one
  attempt through `windows` MCP's synthetic drag did not register, which says nothing about the
  feature), and the Exit item of the tray menu. Both go into the one visit with the user before
  the next release (rule of 2026-09-30); a failure becomes its own task.
- **Reported by:** the v1.7.0 smoke pass, 2026-10-05.

### T-F336 — `TASKS.md`: entries that look open but are cancelled, superseded or done (P3)

- [x] **Status:** done 2026-10-06 (wave 5 after v1.7.0). Moved by script, text unchanged (line-multiset check over both files): T-F01, T-F04, T-F07, T-F08, T-F13, T-F15, T-F33, T-F34, T-F36, T-F41, T-F42, T-F43 and T-F96, to `docs/TASKS_DONE.md`, section "Graduated 2026-10-06 (T-F336)". T-F04 and T-F15 said "future" but shipped as T-F47/T-F49/T-F105 and T-F129; T-F36 is superseded by the routers (T-F85) and the Format list (T-F105). Left in place: T-F114 (`[~]`, its run on a second machine is still not done) and T-F226 (`[~]`, diagrams 5, 8 and the rest of 3). The two `CLAUDE.md` pointers to T-F96 follow it. **Earlier status:** open. Their `Status` line starts with `[ ]` although the text says cancelled,
  superseded or shipped: T-F01, T-F04, T-F07, T-F08, T-F13, T-F15, T-F33, T-F34, T-F41, T-F42,
  T-F43; check T-F36, T-F96, T-F114 and T-F226 the same way. Move them to `docs/TASKS_DONE.md`
  by script with the text unchanged (line-multiset check, as on 2026-10-05), so that a scan of
  open tasks lists only open tasks.
- **Reported by:** the backlog review, 2026-10-05.

### T-F339 — Operation window: UI Automation lists the result text twice (P3, accessibility)

- [x] **Status:** done 2026-10-06 (wave 3 tail, e93939b). The scroll viewer is out of the control view (`AccessibilityView.Raw`). Device (dev 1.7.1.1, uk-UA, `--test plain.zip`): the control-view dump has the result text once (Text), the scroll bar and the Close button are still there; the raw view still has the named pane. **Earlier status:** open. Found in wave 3 after v1.7.0 (2026-10-06, dev 1.7.1.0 build 05:26): after
  `--extract-here` of an encrypted `.7z` the window's UIA tree has the message once as the name of the
  `ScrollViewer` pane and once as the `TextBlock` inside it, so a screen reader can read it twice.
  It is drawn once (region capture). Give the scroll viewer no name of its own, or take it out of
  the control view, and check with a UIA dump. Same class as T-F304.
- **Reported by:** wave 3 device pass, 2026-10-06.

### T-F340 — Operation window: brushes read in code do not follow a live theme change (P3)

- [x] **Status:** done 2026-10-06 (wave 3 tail, e93939b). Every brush set in code is read again on `ActualThemeChanged` (`ApplyThemeBrushes`); a new lookup in `Application.Current.Resources` does give the new theme's brush. Device (dev 1.7.1.1, conflict prompt open, app theme switched dark to light by the registry value plus the `ImmersiveColorSet` broadcast, pixels sampled from a screen capture): card fill 2F2A26 to FDFBF8, secondary labels from light grey to dark grey, text and Mica background followed. The non-Mica background was checked in a local build with Mica forced off: 202020 to F3F3F3, cards 2B2B2B to FBFBFB. With the re-read taken out of the handler (same build otherwise) the cards stayed a dark-theme fill over the light window (F9F3EA) and the two card labels became unreadable (pixel brightness 242..252 on a light card), so the fault was real in the conflict prompt. Not checked: Windows 10 itself. **Earlier status:** open. Same cause as T-F327: `OperationWindow.Brush(key)` reads
  `Application.Current.Resources` once, so the secondary text, the card and the status colours keep
  the start theme, and so does the background where Mica is not supported (Windows 10; the code
  path could not be run on this Windows 11 machine). Not seen as a fault on the device: the result
  view checked in wave 3 uses none of those brushes. The window lives for one operation, so the
  theme has to change during it. Fix by re-applying the brushes on `ActualThemeChanged`; check on
  Windows 10 or with Mica forced off.
- **Reported by:** wave 3 closing review, 2026-10-06.

### T-F341 — App: a ZIP that fails to list shows the .NET exception text, not a translated message (P3)

- [x] **Status:** done 2026-10-06 (wave 3 tail, e8d6532). `ListEntriesAsync` returns `ZipCorrupted` for a reader failure that carries no code of Core's own (tests fail with the new catch disabled: a ZIP signature with no central directory, and a real ZIP cut at 1/2 and at 9/10). Device (dev 1.7.1.1, uk-UA): the Archive Browser's error dialog on that file reads the Ukrainian text Test shows; `pakko l` and `pakko t` both print "File has ZIP signature but appears corrupted or incomplete.", exit 2. **Earlier status:** open. Found in wave 3 after v1.7.0 (2026-10-06, uk-UA): opening a `.zip` with a
  valid signature and no central directory in the Archive Browser shows the error dialog with
  "End of Central Directory record could not be found." (English, from .NET). The Explorer "Test"
  command on the same file says it in Ukrainian ("the file has a ZIP signature but looks damaged
  or incomplete"). So the listing failure reaches the App with no `MessageCode`
  (`ArchiveListResult.ErrorText` is null and `ErrorMessage` is the raw text). Give
  `ListEntriesAsync` the same code the Test path uses; check `pakko l` on the same file.
- **Reported by:** wave 3 device pass, 2026-10-06.

### T-F344 — A ZIP created from a drive root has entry names Pakko refuses to extract (P1)

- [x] **Status:** done 2026-10-06 (wave 5 after v1.7.0). Found in T-F285's device pass:
  `pakko a out.zip P:\` wrote `/r.txt`, `/sub/s.txt` (7za shows `\r.txt`); `pakko t` said the
  archive was fine and `pakko x` refused every entry ("has an unsafe path"), so a whole-drive ZIP
  made by Pakko could not be opened by Pakko. Cause: a root has no name, the entry prefix is
  empty, and both ZIP writers joined prefix and path with "/". Fix: `EntryNameUnder`. Tests first
  (`DriveRootSourceTests`, 3 files for the sequential writer and 73 for the parallel one, both
  red), then extraction of the result. Device (Release `pakko.exe`, `subst` drive): `pakko a`
  writes `r.txt`, `sub\s.txt` (7za `l -slt`), `pakko x` exits 0 with the files out; the archive the
  earlier build wrote extracts with 7za. Shipped in every release with drive-root support (T-F99).
- **Reported by:** wave 5 device pass, 2026-10-06.

### T-F345 — ZIP from a drive root packs the OS's own entries and reports their errors (P3)

- [x] **Status:** done 2026-10-06 (wave 5 after v1.7.0; goes into v1.7.2 on the user's word). `DirectoryWalker` leaves out a drive or share root's own entries that are both Hidden and System, so ZIP creation, the hash of a drive and the size shown for it follow the rule TAR creation and the App's browser have; deeper down such an entry is walked (a `desktop.ini`). An empty root - a new USB stick holds only `System Volume Information` - writes no archive and says so: new message `NothingToArchive` ("Nothing to compress: {0}", 37 locales), a skip, so the outcome is "nothing done" (`pakko` exits 1); before, ZIP wrote one entry named "/" and TAR did nothing in silence. Tests first (`DriveRootSourceTests`: both ZIP writers, and the empty root in ZIP and TAR). Device (dev 1.7.1.1, uk-UA, `subst` drive): `pakko a` of a root with `pagefile.sys` and `System Volume Information` writes `r.txt`, `sub\s.txt`; `pakko h` lists the same two; an empty root gives `pakko: skipped: P:\: Nothing to compress: P:\`, exit 1, no file, in ZIP and TAR; Explorer's helper shows "Пропущено (1): ... Немає чого стискати: P:\". Seen there: the line starts with an empty name, since a root has no file name (any skip of a root shows so; not changed). **Earlier status:** open. A ZIP made from a drive root walks `System Volume Information`,
  `$Recycle.Bin`, `pagefile.sys` and the other entries marked Hidden and System: unreadable ones
  become errors on every NTFS drive, readable ones are packed. TAR creation leaves them out at a
  root since T-F285, and the App's browser hides them (T-F324). Give the ZIP walk the same rule
  for the top level of a root (`DirectoryWalker` has to skip the folder, not only its entry), and
  decide whether an empty drive root should write the single entry "/" it writes now.
- **Reported by:** wave 5 device pass, 2026-10-06.

### T-F342 — Create 7z archives through tar.exe (v1.9, user-requested)

- [ ] **Status:** open. Requested by the user 2026-10-06: Windows now writes 7z by default, so
  Pakko should offer it next to ZIP and the tar family (App Format list, Explorer "Add to X.7z",
  `pakko a -t7z`).
- **Measured 2026-10-06 on Windows build 26300** (`C:\Windows\System32\tar.exe`, bsdtar 3.8.8 /
  libarchive 3.8.8): `tar --format=7zip -cf out.7z a.txt` and `tar -a -cf out.7z a.txt` both write
  a real 7z (signature `37 7A BC AF 27 1C`, method LZMA:23; the vendored `7za t` says "Everything
  is Ok"). A plain `tar -cf out.7z` still writes ustar under that name. This replaces the
  hard constraint in `CLAUDE.md` ("libarchive has no writer for 7z"), which was true for the
  tar.exe checked for T-F50 — correct that line in the same commit as the feature.
- **Open before design (Plan mode + advisor):** which Windows builds ship a tar.exe with the 7zip
  writer (the manifest's minimum is 17763) — detect it in `TarCapabilities` and hide the format
  where it is missing; compression level and method options tar.exe accepts for 7zip; no password
  (libarchive's 7zip writer has no encryption — confirm, and keep "ZIP only" for encryption);
  RAR stays read-only. Creation runs unsandboxed like T-F105 (trusted local input).
- **Reported by:** the user, 2026-10-06.

### T-F343 — `pakko` in place of 7-Zip's console program: measure how compatible it is (v2.0, user-requested)

- [ ] **Status:** open. Requested by the user 2026-10-06. One of the project's goals is a console
  replacement for 7-Zip that is as close to the original as it can be in commands and abilities.
  This task measures the distance instead of guessing it: put `pakko.exe` where a real caller
  expects `7z.exe` / `NanaZipC.exe` (NanaZip's own front ends and scripts first, then common
  third-party callers), run them, and record every command, switch, exit code and output line
  that differs.
- **Outcome:** a compatibility table in `docs/CLI.md` (works / differs / missing, per command and
  switch), and a follow-up task for each gap worth closing. `docs/CLI.md` today says "familiar but
  distinct, not a drop-in" on purpose; this task is where that position is re-decided with
  numbers. T-F342 (7z creation) removes the largest known gap first.
- **Method to settle in Plan mode:** NanaZip's GUI loads its backend in-process (the 7-Zip DLL
  interface), so substituting an exe may only be possible for callers that spawn the console
  program — read NanaZip's real source before choosing the callers. Pakko's rules stay: no
  third-party compression code, extraction of tar-family/7z/RAR only through the sandboxed
  tar.exe.
- **Reported by:** the user, 2026-10-06.

## Performance wave (T-F346–T-F359)

Opened 2026-10-06 from a measured study of start-up and resource use (installed dev 1.7.1.1 x64,
warm starts, 12 cores, Defender on; EventPipe profiles, A/B through environment variables, 7za on
the same data). Baseline: App process start to visible window ~600 ms (CPU ~1000 ms, 138 MB
working set, 60 MB private, 38 threads); Explorer "Open" ~860 ms; Explorer "Extract here" of 200
small files: files written at ~550 ms, Shell exits at ~780 ms. Runtime switches (TieredPGO,
gcConcurrent, gen0size, ConserveMemory) change neither time nor memory and are not tasks. Rule
for every task here: behaviour does not change, only when the work happens; the numbers above are
re-measured with T-F346's script before and after.

### T-F346 — A script that measures start-up, so a change can be compared (P2)

- [x] **Status:** done 2026-10-06. Baseline on dev 1.7.1.1 (medians, 7 counted launches): App window 668 ms, CPU 1047 ms, working set 138 MB, private 61 MB, 38 threads; Explorer Open 873 ms; Extract here: files 577 ms, Shell exit 843 ms; `pakko --help` 166 ms, `pakko l` 311 ms. Run under Windows PowerShell 5.1; PSScriptAnalyzer clean. **Was:** `scripts/Measure-Startup.ps1`: N warm launches of the installed package,
  median and minimum of (a) App start to visible window, (b) `Archiver.Shell.exe --open-ui
  --browse` to visible App window, (c) `--extract-here` of a generated 200-file ZIP: files written
  and Shell exit, (d) `pakko --help` and `pakko l`; plus working set, private bytes, threads and
  CPU of the App two seconds after the window shows. Prints one table; no pass/fail threshold
  (machine-dependent). Used before and after every task in this section.
- **Reported by:** performance study, 2026-10-06.

### T-F347 — App: the tar.exe probe blocks the UI thread before the window exists (P2)

- [x] **Status:** done 2026-10-06. `Measure-Startup.ps1` on the installed package, before -> after (medians of 7): App start to visible window 668 -> 591 ms; CPU, memory unchanged (one more thread while the probe runs). No App test host exists, so the checks are the full default suite (green), and on device (dev 1.7.1.1, uk-UA): a .7z in a folder with a space and Cyrillic opens in the browser with its three entries (Explorer Open route), the same .7z plus a .zip in the pending list give Extract as the primary action, Shell extract-here on .zip/.7z/.tar.gz and `pakko l/t/x/a/i` work. `TarSandboxedService` is built by hand before the container so the probe can start at once (`docs/ARCHITECTURE.md`). **Was:** `App.ConfigureServices` runs `DetectCapabilitiesAsync().GetAwaiter()
  .GetResult()`: `WinVerifyTrust` on tar.exe (~47 ms) plus `tar.exe --version` (~30 ms) plus
  first-call costs, ~95-108 ms of the ~600 ms start, with a 5 s worst case (the probe's timeout).
  Start the probe on the thread pool at the top of `ConfigureServices` and let the
  `TarCapabilities` singleton factory take its result; `MainViewModel` is first built ~380 ms
  later, after `LoadComponent`, so the value is ready. Types stay synchronous, the signature check
  stays, the worst case equals today's.
- **Must not change:** what `TarCapabilities` holds; `DisableTarExtraction` never starts tar.exe
  (T-F261); a cold-start activation with a .7z/.rar still classifies it (T-F100, T-F03).
- **Reported by:** performance study, 2026-10-06.

### T-F348 — `LaunchArguments`: reflection JSON costs ~37 ms in Shell on every "Open" (P3)

- [x] **Status:** done 2026-10-06. Tests first, green on the old code: the payload pinned character for character (spaces, Cyrillic, quotes, `<&'+>`, tab, U+00A0, an emoji, a trailing backslash), strict-JSON refusals, escaped and raw non-ASCII parse alike; mutation (encoder dropped) fails two tests. Explorer Open, Shell start to visible App window: 873 -> 753 ms together with T-F347 (~77 ms of it is T-F347's). **Was:** `LaunchArguments.Format`/`TryParse` use reflection-based `JsonSerializer`
  for a string list; the first call builds the reflection metadata (37 ms in Shell's profile; the
  App's `TryParse` side not measured). Use a source-generated `JsonSerializerContext`, as
  `Archiver.OperationUi.Protocol` does. The argument string must stay byte-identical (a test
  pins today's output for ASCII, Cyrillic, quotes and backslashes before the change), and
  `TryParse` must accept everything it accepts today and still never throw. Also a precondition
  for Native AOT (T-F355).
- **Reported by:** performance study, 2026-10-06.

### T-F349 — Shell, the operation window and `pakko` ship without ReadyToRun (P2)

- **Superseded 2026-10-08 by T-F355:** every exe is Native AOT, ReadyToRun is gone from the build.

- [x] **Status:** closed 2026-10-06. Standalone `pakko.exe`: done (`Publish-Cli.ps1` passes `PublishReadyToRun`; `pakko.dll` and `Archiver.Core.dll` carry R2R code in the CI zips for win-x64 and win-arm64, checked by PE header). The five DLLs in the package: **not done, the gain is too small.** Measured after T-F348/T-F350/T-F351 in two unpackaged copies of the installed package, one with R2R-published `Archiver.Shell.dll`, `Archiver.OperationUi.Protocol.dll` and `pakko.dll` (two alternating rounds of 15): `pakko l` 157/163 -> 149/162 ms - under the 20 ms set as the bar for changing three build files, and a clean comparison. Shell extract-here 430/508 -> 416/501 ms (CPU 547/688 -> 531/625 ms) is **indicative only**: outside the package the helper crashes at start, so those runs took the Win32 failover path, not the shipped one. **Not measured on the real path:** `Archiver.Shell.dll`, `Archiver.OperationUi.Protocol.dll` (the study's ~110 ms of jitted `FrameCodec..cctor`) and the helper's three DLLs. Reopen with a packaged A/B (two installs, `Measure-Startup.ps1`: Shell exit and the conflict prompt) if that is wanted. **Was:** Checked in the installed package by PE header: `Archiver.Shell.dll`,
  `pakko.dll`, `Archiver.OperationUi.dll`, `.Core.dll`, `.Protocol.dll` have no R2R code
  (`Deploy.ps1`/`CI-Build-Msix.ps1` build them with `dotnet build`; R2R runs only in publish);
  `Publish-Cli.ps1`'s standalone `pakko.exe` has none either, Core included. Measured: standalone
  `pakko l` 304 -> 259 ms and `t` 248 -> 213 ms with R2R; `FrameCodec..cctor` in Shell ~110 ms
  jitted. Standalone CLI: `PublishReadyToRun` in `Publish-Cli.ps1`. Package: get R2R images of the
  five assemblies without moving the paths `Archiver.App.csproj`'s `Content Include` items and
  the two scripts name (T-F128's stale-DLL trap) — how is settled in Plan mode.
- **Verify:** R2R header of each DLL inside the installed package and inside a CI-built MSIX, not
  file times; every Explorer command and `pakko` from the package still run.
- **Reported by:** performance study, 2026-10-06.

### T-F350 — Shell and `pakko` probe tar.exe for a ZIP (P2)

- [x] **Status:** done 2026-10-06. `PakkoServices` hands the probe to an `internal` constructor of each router; `ArchiveFormatPolicy.ClassifyAsync` detects each path once and awaits the probe only for a tar-family format that policy leaves open (one predicate, shared with `GetRefusalReason`). Public constructors and the App's DI are unchanged. Tests first (`LazyTarProbeTests`): no probe for ZIP or for a policy-refused format (red before), one probe across operations, Cancel ends the wait, and 72 verdicts with their texts (9 formats x 4 policies x 2 capability sets) pinned on the old code. Installed package, before -> after: Extract here files written 582 -> 459 ms, `pakko l` 314 -> 197 ms, conflict prompt 781 -> 691 ms, operation window on a long extraction 1929 -> 1775 ms (a later full run on a busier machine: 496, 221, 694, 1794 - the spread between runs is 40-60 ms). **Was:** `PakkoServices.CreateExtractionRouterAsync`/`CreateListingRouterAsync`/
  `CreateScanServiceAsync` await the tar.exe probe before any work, so a ZIP-only command pays
  ~70-100 ms for nothing (`pakko l x.zip` 300 ms against `--help` 90 ms; "Extract here" 73 ms).
  Probe only when an input is not a ZIP. Touches public constructors in `Archiver.Core`
  (routers take `TarCapabilities`) — Plan mode + advisor before and after.
- **Must not change:** which formats are accepted or refused and with which message; the probe
  runs at most once per process (T-F85) and never under `DisableTarExtraction` (T-F261); a file
  whose content is not what its extension says is still routed where it is today.
- **Reported by:** performance study, 2026-10-06.

### T-F351 — Explorer commands start the WinUI operation window even when it is never shown (P2)

- [x] **Status:** done 2026-10-06, narrowed: the helper still starts at `Begin` (starting it later is T-F356). A clean end (`Complete(null)`, or `Dispose` without `Complete`) that comes before `HelperReady` ends the helper at once instead of waiting for it to start and close - the helper reads nothing before it sends `HelperReady`, so no window can be up. A result, a prompt and a ready helper take the old path. Tests first (both red before). Installed package: Extract here, Shell exit 697 -> 455 ms (files written 451; 512 and 496 in the later full run - the gap between the two is what the change removed, ~240 -> ~15 ms); conflict prompt and the long-extraction window unchanged (679, 1763 ms). On device: results after a fast operation (Test, a damaged ZIP), password and conflict prompts, Cancel on a long extraction (nothing left behind), helper killed mid-operation -> Win32 window finishes the work, real Explorer menu on ZIP and .7z. **Was:** The helper process starts at `Begin`; the window shows only after 1 s
  (`OperationWindowModel.ShowDelay`), a prompt or a result. On a fast operation Shell then waits
  ~160-230 ms in `Session.Complete` for a helper that is still starting, only to close it, and a
  whole WinUI process (~300 ms CPU) is spent beside the extraction. Start the helper after a
  short delay, or at once when a prompt or a result message is due. Plan mode + advisor.
- **Must not change:** the window still appears no later than today on a long operation; prompts
  (conflict, password, bomb), the result text, cancel, the Win32 fallback and failover (T-F268),
  foreground hand-over (T-F253).
- **Reported by:** performance study, 2026-10-06.

### T-F352 — ZIP creation of a few large files runs on one core (P2)

- [x] **Status:** done 2026-10-06. On device (dev 1.7.1.5, NVMe): `pakko a` on 3 x 100 MB (entries stored, 4.7 s; 8.9 s for 300 MB + a small file, which stays sequential), on text and on 70 small files, each through `7za t`; round trip byte-identical; Shell `--archive` with the operation window; the App window on 600 MB (progress and speed shown, done in 5 s); Cancel at 2-2.7 s in the operation window and in the App leaves no archive and no chunk folder. Not checked: a spinning disk (none on the dev machine). A `SingleArchive` run also takes the parallel writer when the level is not NoCompression, at least 8 MiB lie beside the largest file and at least a quarter of it, and the volume has room for twice the sources, and the source and destination disks report no seek penalty (`IO/DiskSeekPenalty`: a spinning disk, a share or a disk that does not answer keeps the sequential writer) (`ZipArchiveService.UsesParallelWriter`); the writer is unchanged. Not a total-size rule: one large file gains nothing and pays for a second copy. Measured, sequential -> parallel: 3 x 100 MB text 4.1 -> 1.7 s, incompressible 8.6 -> 3.6 s (7za 2.9 s); 300 MB + 1 KB stays sequential. Such archives now have what archives of more than 64 files already have: an entry Deflate did not shrink is stored, entries carry the Archive attribute. The bar waits at 99% for 0.5 s at 3 x 100 MB. Tests first (`ZipArchiveServiceWriterChoiceTests`, 6 red before; five mutants killed), `ArchiveAsync_FewLargeFiles` vs 7za ratio 1.2. Details: `docs/DECISIONS.md`'s T-F352 entry. **Was:** The parallel writer starts at 64 files
  (`ParallelPipelineFileCountThreshold`). 3 x 100 MB: Pakko 7.7 s wall / 8.3 s CPU (11 MB
  private), 7za 6.6 s wall / 19.4 s CPU. Add a total-size criterion so a few large files take the
  parallel path (T-F35, proven by T-F114). Plan mode: the threshold, temp-file disk space (the
  pre-check exists), progress.
- **Verify:** `Category=Slow` and `Category=VeryLarge` (T-F114 ratios), archives checked with
  `7za t`, entry data identical to the sequential writer's.
- **Reported by:** performance study, 2026-10-06.

### T-F353 — ZIP extraction of many small files: per-entry overhead (P3, measure first)

- [x] **Status:** closed 2026-10-06 without a code change. Each lead alone is under the 5% bar; all three together do pass it on an archive without the mark (-7%, ranges do not overlap) and are left out on purpose: that is ~0.18 ms per file (~35 ms on 200 files), it disappears on a downloaded archive (-5%, inside the spread), and it means changing the MOTW write and the content -> mark -> time order (T-F45, T-F298) - the larger costs below are elsewhere (T-F358). Measured through temporary switches, 5000 small files, Release, 7 rounds alternating with 7za, Defender on (medians; spread within a variant 3-6%). Archive without `Zone.Identifier`: baseline 12.80 s; mark read once per archive 12.59 (-1.6%); synchronous destination stream 12.77 (-0.3%); time set through the open handle 12.49 (-2.4%); all three 11.88 (-7%); 7za 6.20. Archive with the mark: 22.36 / 21.59 / 21.80 / 21.89 / 21.27 s (-5% for all three, inside the spread); 7za 6.30, which writes no mark. What the numbers do show: a file costs ~2.5 ms here against 7za's ~1.2 ms, and writing the mark adds ~1.9 ms per file (a second stream created and closed under Defender) - neither is one of the three leads; both are T-F358. **Was:** 3000 small files: Pakko 5.1-5.2 s wall / 5.8 s CPU, 7za 4.1-5.5 s /
  3.9-4.6 s — wall time is Defender and the disk, so the headroom is small. Leads from the
  profile, none measured alone: `TryPropagateMotw` opens `archive:Zone.Identifier` once per entry
  (an exception per entry when the archive has none); the destination `FileStream` has its own
  80 KB buffer and `useAsync: true` under `CopyToAsync`'s buffer; `FileTimes.TrySetFile` reopens
  the file by path. A/B each against 7za in the same run; keep only what shows. T-F298's order
  (ADS first, time last) and T-F45 stay.
- **Reported by:** performance study, 2026-10-06.

### T-F357 — The parallel ZIP writer writes an incompressible file three times (P3)

- [x] **Status:** done 2026-10-08 (the user's "yes, if nothing breaks and nothing depends on the
  current shape"). An unencrypted entry stored as it is - one Deflate did not shrink, or any at
  NoCompression when this writer runs at all (over 64 files; three files at NoCompression take
  `ZipArchive`, which already wrote them once) - is no longer written into its chunk a second time: the worker keeps the
  source's read handle (sharing read only, so the bytes its CRC describes cannot change) and the
  drain copies the source into the archive, checking length, end of file and CRC again
  (`WorkResultKind.SourceStored`, `ZipEntryWriter.WriteStoredEntryFromSourceAsync`). Encrypted
  entries keep the chunk (salt and authentication code are made there). The first Deflate pass
  still writes its chunk - deciding earlier would change the output. Archive bytes unchanged:
  SHA-256 of 11 archives (4 levels through the writer directly, Fastest single and separate
  through `ArchiveAsync`; big random and text, small, empty) identical before and after. 3 x 100
  MB random, Release: written 944 -> 629 MB at Optimal, 961 -> 646 MB at Fastest, time the same on
  NVMe (CPU-bound). Checked on device (1.7.1.9, the installed `Archiver.Shell.exe --archive`): 3 x
  40 MB random + text + 100 small + empty -> `7za t` OK, Store/Deflate as expected, 105 files equal
  after `7za x`, no `.pakko-tmp-*` left, sources deletable at once.
- **Was:** open. A file Deflate does not shrink (video, photos, archives) is compressed
  into a chunk file, rewritten there as Stored (T-F299), then copied into the archive: 3 x 100 MB
  writes ~900 MB for a 300 MB archive; a compressible file is written twice. On an SSD that is
  wear and time, on a spinning disk more. Leads: decide Stored from the first block(s) before
  writing a chunk; write the largest pending entry straight into the archive. (The disk question
  for the path of more than 64 files is T-F359.) Plan mode; the writer's output must not change.
- **Reported by:** the user's question on disk types, 2026-10-06 (T-F352).

### T-F358 — ZIP extraction of small files costs twice 7za's time, and the mark nearly doubles it again (P2, measure first)

- [x] **Status:** done 2026-10-08. Profiled first (2000 small files, Release, per file): the
  file was opened three times - content, mark, time - and every open and close goes through
  Defender's filter (create ~350 us, close ~280, mark ~2200, time by path 130-185); for an archive
  without the mark each file also paid a failed open and an exception. Now the archive's mark is
  read once (`ArchiveEntrySecurity.ReadMotw`), and the mark and the entry's time are set before the
  file's one close (`ZipArchiveService.MarkAndSetTime`: flush, mark, time through the handle); the
  order content -> mark -> time (T-F45, T-F298) is kept, `SECURITY.md` is unchanged. Same session,
  medians of 7 rounds alternating with 7za: archive without the mark 1116 -> 987 us per file (7za
  1006 -> 993), with the mark 2780 -> 2481 (7za writes none). The mark itself (~1.5 ms, Defender
  scanning the new stream) stays; writing it before the content was faster still (-5%) and was
  not taken, since it changes the order. Checked on device (1.7.1.8, the installed
  `Archiver.Shell.exe --extract-folder` started directly, not through the menu): a marked ZIP, .7z
  and .tar.gz give every file the mark and the entry's time, an unmarked ZIP gives none and the
  time; the same into a `\\localhost\C$` share, with a zero-byte and a 3 MB entry.
- **Was:** open. From T-F353's measurement (5000 small files, Release, Defender on): 12.8 s
  against 7za's 6.2 s - ~2.5 ms per file against ~1.2 ms; an archive carrying `Zone.Identifier`
  (every downloaded one) takes 22.4 s, ~1.9 ms more per file for the second stream. Not profiled:
  where the 2.5 ms goes (staging folder and commit, per-entry path checks, the stream stack),
  and whether the mark can cost less without weakening T-F45 (what 7-Zip and Explorer do per
  file; one write instead of open/copy/close). Profile first; any change to the MOTW path needs
  the user's word and `SECURITY.md`'s owner rule.
- **Reported by:** T-F353's measurement, 2026-10-06.

### T-F359 — The parallel ZIP writer on a disk with a seek penalty: large files take turns (P2)

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

### T-F360 — Extract: an "apply the download mark" checkbox, on by default (P2)

- [x] **Status:** done 2026-10-08 (DECISIONS.md's T-F360 entry). Core: `ExtractOptions.
  ApplyDownloadMark` (default true), `GroupPolicyOptions.MotwModeSetByPolicy` +
  `EffectiveMotwMode(applyMark)` (the policy wins), public `ArchiveDownloadMark.IsPresent`; App:
  checkbox + note (cost / risk / policy) in the destination card, logic in App.Core's
  `DownloadMarkOption`; CLI: `x -snz`/`-snz1`/`-snz0`, `-snz2` refused. Checked on device (1.7.1.10):
  marked ZIP shows it checked with the cost note, unchecking shows the risk note and extracts
  unmarked files, checked again on reopening and marks them, an unmarked ZIP shows no checkbox, a
  browser preview with it unchecked is still marked. Policy on device (1.7.1.11, real HKLM value,
  the user approved UAC): `EnforceMOTW=1` - checkbox locked and checked with the policy note,
  files marked; `pakko x -snz0` warns and marks. `EnforceMOTW=0` - locked and unchecked, files
  unmarked; `pakko x -snz` warns and does not mark. The value was removed afterwards. The user's
  idea: a checkbox, on by
  default, that applies the archive's `Zone.Identifier` to the extracted files; unchecking it
  shows the risk under it. The user's decisions: unchecked means no mark at all (not
  `UnsafeExtensionsOnly`, whose list has no Office, PDF or ISO types); Group Policy's `MotwMode`
  (T-F51) wins - when set, the checkbox is locked and says so; in the App's Extract wizard, the
  Shell's "Extract..." dialog and a `pakko x` switch ("Extract here" has no dialog and keeps the
  mark); shown only for an archive that carries the mark. The mark costs ~1.5 ms per file (T-F358),
  ~15 s per 10,000 small files - the text may say so. All 37 locales. Changes the MOTW hard
  constraint in `CLAUDE.md` and `SECURITY.md` - the user gave permission for this task.
- **Reported by:** the user, 2026-10-08.

### T-F354 — App: load the hidden parts of `MainWindow.xaml` on first use (P3, measure first)

- [ ] **Status:** open. `LoadComponent` is ~148 ms of the start; browse mode and the password
  panel are hidden then. `x:Load` could defer them — estimate 30-60 ms, not measured. Risk: the
  layout history of T-F106/T-F05 (blank rows) and `x:Bind` targets inside deferred elements. Do
  only with an on-device A/B that shows the gain; otherwise close as not worth it. Measure against
  the Native AOT App (T-F355) - the start is already ~150 ms shorter.
- **Reported by:** performance study, 2026-10-06.

### T-F355 — Native AOT: a spike for `pakko`, Shell and the App (decision needed)

- [x] **Status:** done 2026-10-09 (implemented and smoke-tested 2026-10-08). Spike (branch
  `spike/native-aot`) numbers, x64 medians: `pakko x` (ZIP) 133 -> 34 ms, Shell "Extract here"
  (ZIP) 151-192 -> 32 ms, App window 587 -> ~430 ms, App working set 139 -> 105 MB, MSIX 62.9 ->
  14.8 MB (346 -> 27 files), CLI zip 37.8 -> 2.8 MB. **User decision 2026-10-08: the whole project
  on Native AOT** (it is all or nothing: an AOT App leaves no .NET runtime in the package for the
  satellites). All four exes `PublishAot`, the five libraries `IsAotCompatible`; the `CLAUDE.md`
  `PublishTrimmed=false` constraint replaced by the AOT rule. Three AOT failure classes found and
  fixed (`docs/DECISIONS.md`); guarded by `PackagingManifestTests`, `WinUiAotSourceGuardTests`
  and the Subprocess layer against the AOT `pakko.exe` in CI. **Smoke 2026-10-08** (agent, `windows`
  MCP, local Deploy 1.7.1.18 — `build.yml` builds no artifact for a branch): every Explorer menu
  item through the real context menu, the WinUI operation window (progress, Cancel, conflict,
  password, bomb, results) and its Win32 failover after killing the helper, 1000 paths over
  stdin, the tar.exe sandbox (7z, tar.gz), AMSI (EICAR found); App browse/nested/preview, "Up" to
  "This PC", AES ZIP and TAR.XZ, all ContentDialogs, the download-mark checkbox, Test/Scan/Hash,
  About, Recycle Bin, light theme; CLI pipes, `h -si`, `-snz0`, Ctrl+C -> 255; no Pakko crash in
  the event log. The canary on the branch ran the AOT `pakko.exe` 77/77 natively on an ARM64
  runner too. Not done here: Group Policy (HKLM needs UAC), keyboard/narrow window, WACK, the
  ARM64 package on a device (no hardware); Group Policy, WACK, drag-drop and the language change
  wait for the pre-release checks. **CI artifacts after the merge** (run 37841989204, 4cbd691):
  both bundles one inner package, 24 files, 14.1/13.7 MB, 37 languages, no `coreclr.dll` and no
  managed `.dll`, all four exes native with the same icons and versions as the local build; the
  CLI zips hold `pakko.exe` alone, and the x64 one passed the Subprocess layer 77/77.
  One unrelated finding: T-F362.
- **Reported by:** performance study, 2026-10-06.

### T-F356 — Start the operation-window helper only when the operation is not fast (decision needed)

- [x] **Status:** done 2026-10-06. Extract and Archive start the helper after 0.5 s, or at once
  for a prompt or a result; Test, Scan and Hash at once, as before
  (`DeferredOperationSession`). Measured on the installed package: Extract here (200 files) files
  written 451 -> 401 ms, Shell exit 464 -> 403 ms; on two cores 761 -> ~380 ms (20 files: 489 ->
  ~210 ms). The window on a long extraction shows when it did (1774 -> 1785 ms: `Begin` carries
  the elapsed time and the helper's 1 s show delay is shortened by it). Cost: a conflict prompt in
  the first half second 676 -> 731 ms. Condition (1) below: the helper is started with a handle
  list (`HandleListProcess`), so it cannot take a `tar.exe` pipe. On device (dev 1.7.1.7): fast
  extract and archive start no helper process; 6000 files from a ZIP and from a .7z show the
  window and finish (the .7z run alone, by parent process: the helper appeared at 0.8 s with
  `tar.exe` alive from 0.4 to 4.4 s); from Explorer's real context menu the window of a long
  extraction and the conflict prompt are the foreground window when they show
  (`GetForegroundWindow`, T-F253); Cancel leaves only the archive;
  conflict Skip and Overwrite, password prompt then result, Test result and Close - each through
  the window's own buttons; no helper process is left after Shell exits. Not checked on device:
  the helper failing to start (tests only). Tests first, twelve mutants caught. Details:
  `docs/DECISIONS.md`'s T-F356 entry. **Was:** open, outside the wave. Split from T-F351: starting `Archiver.OperationUi` only
  after ~0.5 s (or at once for a prompt or a result) would save a whole WinUI process on every
  fast Explorer command. Two conditions before it can be done, each with numbers: (1) a helper
  started in the middle of the work can coincide with a `tar.exe` launch — the pipe handles are
  inheritable at that moment, so the two launches must be serialized or the handles listed
  explicitly; (2) an operation of 0.5-0.9 s would get its window later than today (helper start
  ~0.8 s on top of the delay) — measure with `Measure-Startup.ps1`'s operation-window scenarios
  (baseline: window at ~1.9 s on a long extraction, conflict prompt at ~0.8 s). Plan mode +
  advisor.
- **Reported by:** performance study, 2026-10-06.

### T-F361 — winget: a `WinGet\Links` symlink for the AOT `pakko.exe` (P3)

- [ ] **Status:** open. The winget manifest puts the install folder on `PATH`
  (`ArchiveBinariesDependOnPath: true`) because the pre-AOT apphost looked for `pakko.dll` next to
  the symlink. The Native AOT `pakko.exe` (T-F355) is one file, so winget's ordinary symlink should
  work and leaves no stale `PATH` entry after uninstall. Change `New-WingetManifest.ps1`, check with
  `winget install --manifest` (admin, `LocalManifestFiles`), update `docs/CLI.md`.
- **Reported by:** T-F355, 2026-10-08.

### T-F362 — a renamed archive of a compound extension gets its number in the wrong place (P3)

- [x] **Status:** done 2026-10-08 (user: "do it now"). Creating `src.tar.xz` where one exists, with
  "Rename (add a number)", named the new archive `src.tar (1).xz`, not `src (1).tar.xz`:
  `ArchiveNaming.GetUniqueName` split on `Path.GetExtension` (the last dot only), while
  `ArchiveNaming` already knew the compound extensions (T-F103). Not new with AOT: Core code, found
  by the T-F355 smoke. Now a compound tar extension is one extension for every rename-on-conflict
  path (archive creation, extraction, duplicate entry names). Tests first (8 red): every compound
  extension and case, a dotted stem; names that only look compound (`notes.tar.gz.txt`, `.tar.gz`
  alone) keep the old rule; `TarSandboxedService` creating over an existing `out.tar.xz`. Two
  mutants killed (no length guard, case-sensitive match).
- **Reported by:** T-F355 smoke, 2026-10-08.

### T-F363 — Windows App SDK 2.x, SDK BuildTools 10.0.28000 and C# 14, one wave (P3)

- [~] **Status:** Part B done 2026-10-09; Part A open. **Part B result:** C# 14 set once in
  `Directory.Build.props` (`Repo_PinsCSharp14Once`); the predicted `StdinPathList.cs:43` break did
  not happen (the build is clean on SDK 10.0.401, no code change there); the 28
  `[ObservableProperty]` fields are partial properties and `NoWarn MVVMTK0045` is gone (a field
  fails the build); 8 lock objects are `Lock` (`LockSourceGuardTests`); `field` had no candidate
  (every hand-written setter is computed or throws); IDE0330/0340/0360/0032/0031 enforced as
  errors, which turned 4 read-only backing fields into auto properties. Tests, mutants (3) and the
  smoke on the deployed package green. **Part A (packages).** Dependabot proposed both for `Archiver.OperationUi`
  alone (#13 BuildTools 10.0.26100.7705 -> 10.0.28000.2705, #22 Microsoft.WindowsAppSDK
  1.8.260209005 -> 2.5.1); closed, because the two WinUI exes must stay on one Windows App SDK and
  the bump changes the shipped binaries under Native AOT (T-F355). Do both projects in one change:
  read the 2.x release notes (breaking changes, AOT/CsWinRT), build both architectures, run the
  T-F355 smoke rows for the App and the operation window on the deployed package.
- **Part B (C# 12 -> 14, user request 2026-10-08).** `LangVersion` 12 is pinned in
  `Directory.Build.props` and repeated in six `.csproj` files (App, CLI, Core, Shell, CLI.Tests,
  Shell.Tests); the pin was kept by T-F270 only because C# 14 rebinds span calls. Move every
  project to 14 in one commit, taking C# 13 along. Known break, found by reading the official
  breaking-change list against the code: `StdinPathList.cs:43`,
  `MemoryMarshal.Cast<byte, char>(bytes)` on a `byte[]` - C# 14 sees both the `Span` and the
  `ReadOnlySpan` overload (the documented `MemoryMarshal.Cast` case); write `bytes.AsSpan()`.
  Then a full build with `TreatWarningsAsErrors` (new CS9258/CS9272/redundant-`or`-pattern
  warnings become errors), the whole test suite, mutants on the touched code, and the T-F355 smoke
  rows, since the AOT binaries change. Features adopted in the same wave only where the table
  below says so; each adoption is its own reviewed step, not a sweep.

  Every C# 13 and 14 feature (Microsoft Learn "What's new in C# 13/14" and the compiler breaking
  changes for .NET 9 and .NET 10), against Pakko's code (grep of `src/`, 2026-10-08):

  | C# | Feature | Pakko today | Use for us |
  |---|---|---|---|
  | 13 | `params` collections (`params ReadOnlySpan<T>`, `IEnumerable<T>`, ...) | 10 `params object[]` (`CoreMessages.Text/Error/Skip`, `CoreText`, the Shell localizers) | Small: `params ReadOnlySpan<object?>` saves an array per message; only if a profile shows it. Keep arrays otherwise |
  | 13 | `System.Threading.Lock` with the `lock` statement | 37 `lock` statements, 6 `readonly object` lock fields (LogService, CliProgress, AppContainerProfile, DeferredOperationSession, HelperOperationUi, Win32OperationUi) | **Yes**: change the 6 field types to `Lock` - clearer intent, cheaper than `Monitor`, a typed lock cannot be locked by accident on another object. Mechanical, tests exist |
  | 13 | `\e` escape | no ESC literals | None |
  | 13 | Method group natural type improvements | transparent | None (compiler only) |
  | 13 | `^` index in object initializers | not used | None |
  | 13 | `ref` locals / `ref struct` in async and iterators; `unsafe` in iterators | Core works on spans in sync helpers and copies around `await` | Some: lets a span helper live inside an async method without a sync split. Use when touching such code, not as a sweep |
  | 13 | `ref struct` implementing interfaces; `allows ref struct` | no own `ref struct` types | None |
  | 13 | Partial properties and indexers | 29 `[ObservableProperty]` fields (App, `MainViewModel`), `MVVMTK0045` suppressed | **Yes, the main gain**: MVVM Toolkit 8.4 asks for partial properties so CsWinRT sees them - the AOT-safe form (T-F355 research item 4) - and lets `NoWarn MVVMTK0045` go. Convert all 29 (toolkit code fixer MVVMTK0042), device check of every bound list |
  | 13 | `OverloadResolutionPriorityAttribute` | library authors' tool | None |
  | 14 | `field` keyword (field-backed properties) | 14 hand-written `set` accessors in `src/` (grep; which of them only validate or notify is to be read) | **Yes**: drops the backing field where a setter only validates or notifies. Breaks: none found - `field` appears only as a method parameter (`TarHeaderNames`), which stays legal |
  | 14 | Extension members (extension properties, static extensions) | no extension-method classes | None now |
  | 14 | Null-conditional assignment `a?.B = c` | none found by a grep for `if (x is not null)` + `x.P = ...` | Small: readability where it occurs |
  | 14 | `nameof` of an unbound generic (`nameof(List<>)`) | not needed | None |
  | 14 | First-class `Span`/`ReadOnlySpan` implicit conversions | **the one known break** (`StdinPathList.cs:43`); risk of a different overload for array arguments - covariant arrays could throw `ArrayTypeMismatchException` | Gain: span APIs take arrays directly. Cost: review every `MemoryExtensions`/`MemoryMarshal` call on arrays; the tests and the smoke must stay green |
  | 14 | Modifiers on simple lambda parameters (`(text, out result) => ...`) | not counted | Small |
  | 14 | Partial events and constructors | generators (CsWinRT, LibraryImport, JSON) do not need them | None |
  | 14 | User-defined compound assignment operators | no operator types | None |
  | 14 | File-based app directives (`#:package`) | not an app style we use | None (could replace throwaway `.ps1` probes, not needed) |

  Breaking changes checked against the code: `scoped` as a lambda type name (none), `extension` as
  a type name (only locals named `extension` - legal), `partial` as a return type (none),
  `field` inside accessors (none), redundant `is not X or Y` patterns (none), `Enumerable.Reverse`
  on arrays (does not apply on `net10.0`), collection-expression overload changes (spot-check the
  `[]` call sites when building), iterator safe context in `unsafe` classes (none).
- **Reported by:** Dependabot, 2026-10-08 (Part A); the user, 2026-10-08 (Part B).
