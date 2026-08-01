# CHANGELOG.md — Release History

Human-readable summary of what shipped in each tagged release. One section per version tag,
newest first. This file starts at `v1.4.1` — earlier releases (`v1.0.0`, `v1.1.0`, `v1.4.0`) are
not backfilled; see `docs/TASKS_DONE.md` and `git log` if you need that history.

Each entry lists the `T-Fxx` tasks completed since the previous release tag, in plain language —
not a re-statement of `docs/TASKS_DONE.md`'s full acceptance-criteria detail. See `docs/TASKS_DONE.md` for
the technical account of any task named here.

---

## v1.4.6 — 2026-08-01

Bug-fix release — restores full localization to the shipped MSIX. Affects `v1.4.5` (and likely
earlier CI-built releases) too: only English shipped despite 37 supported locales.

- **T-F139** — the MSIX packaging pipeline's default auto-resource-package split
  (`AppxBundleAutoResourcePackageQualifiers=Language|Scale|DXFeatureLevel`) tried to carve each
  locale's resources into its own resource-only package, then silently failed to actually produce
  them — the shipped package ended up with only `en-US`, even though every locale was correctly
  detected during the build. Confirmed on a truly clean build (not just CI) via
  `docs/DECISIONS.md`'s T-F139 entry. Fixed by dropping `Language` from the auto-split qualifier
  list, so all 37 locales are embedded directly in the single package again, matching the
  pre-regression shape. If you installed `v1.4.5` (or possibly earlier), update to this release to
  restore non-English UI.

---

## v1.4.5 — 2026-08-01

CI/tooling release — no user-facing app changes. Builds Pakko's first Microsoft Store submission
pipeline (T-F129).

- **T-F129** — added `build-store-msix` and `bundle-store-msix`, two new `workflow_dispatch`-only
  CI jobs that produce a single, correctly-identified multi-architecture (`x64`+`arm64`) MSIX
  bundle signed with Pakko's reserved Partner Center Publisher identity — distinct from the
  tester-facing `CN=Pakko Dev` cert used everywhere else. Fixed four real, previously-undiscovered
  MSIX/Partner Center packaging quirks along the way (Publisher gets silently rewritten to match
  the signing cert at build time; a `.msixbundle`'s own identity carries no architecture, so
  separate single-arch bundles collide; a re-signed bundle needs its cert trusted, not just
  present, for signature verification to read `Valid`; Partner Center's package-identity
  uniqueness check is scoped to the developer account and survives submission deletion). First
  real Partner Center upload of the resulting package succeeded 2026-08-01. See
  `docs/TASKS.md`'s T-F129 entry for the full trail.

---

## v1.4.4 — 2026-08-01

Security-hardening and CI/tooling release — no user-facing feature changes.

- **T-F135** — SonarCloud static analysis wired into CI (`build.yml`'s `test` job), with code
  coverage fed into the quality gate. Real analysis confirmed against the live SonarCloud
  dashboard, not just a green Actions run.
- **T-F136** — All 269 findings from SonarCloud's first scan individually triaged: real bugs
  fixed, false positives suppressed with a documented reason, genuine refactor-scale debt tracked
  as new follow-up tasks rather than silently dropped.
- **T-F137** — Local static-analysis tooling (SonarLint-equivalent analyzer config) added so the
  same rule set SonarCloud enforces in CI is visible during local development too.
- **T-F138** — Fixed the real defect behind SonarCloud's `S3869` findings: several
  `SafeHandle.DangerousGetHandle()` calls in the AppContainer sandbox's P/Invoke layer
  (`QuarantineAcl`, `SecurityCapabilitiesAttributeList`, `SandboxedProcessLauncher`) are now
  `SafeHandle`-typed parameters instead of raw handle extraction, closing a handle-recycling race
  window during sandboxed `tar.exe` launches. Confirmed via SonarCloud: `bugs: 0`,
  `reliability_rating: A`.
- Dependency hygiene: `CommunityToolkit.Mvvm` version synced between `Archiver.App` and
  `Archiver.App.Core`; `System.Net.Http`/`System.Text.RegularExpressions` pinned to patched
  versions in test projects.
- CI fixes: explicit workflow-level permissions on `build.yml`, cross-platform NuGet restore
  enabled, `global.json` SDK version corrected for `setup-dotnet`, Dependabot reverted to
  security-only updates.

---

## v1.4.3 — 2026-07-26

Point release adding a version flag to the CLI.

- **T-F134** — `pakko -v`/`--version` prints the CLI's own version and exits 0. A released
  `pakko.exe` now always reports the exact release tag it shipped under.

---

## v1.4.2 — 2026-07-25

Point release widening ZIP-format recognition to two more container-file families, plus
Microsoft Store submission preparation.

- **T-F131** — Explorer/file-association recognition of `.jar`/`.war`/`.ear` (Java) and `.apk`
  (Android) as ZIP-format archives, so the full Pakko context menu (Extract/Test/etc.) works on
  them directly.
- **T-F133** — Same recognition extended to `.asice`/`.asics` (ASiC-E/ASiC-S signed containers)
  and `.bdoc` (Estonia's ASiC-E profile), at a user's request.
- **T-F129** — Microsoft Store submission preparation: WACK (Windows App Certification Kit)
  fixes, vector-accurate brand assets regenerated from the canonical SVG source, and an
  `Package.appxmanifest` revision-segment fix required for submission. The Store listing itself
  is not live yet.
- **T-F130** — Fixed intermittent CI test failures in `Archiver.Core.IntegrationTests` caused by
  sandboxed tests racing each other under parallel execution.

---

## v1.4.1 — 2026-07-20

Point release covering Explorer hash commands and a `pakko.exe` CLI addition.

- **T-F128** — Explorer context-menu hash commands: CRC-32/SHA-256 submenu for files, folder
  DataSum/NamesSum, a progress-bar fix, a Size line, full 37-locale localization, and CRC-32
  performance work (intra-file parallel hashing).
- **T-F128/T-F09 follow-up** — `pakko.exe` gained an `h` (hash) command and `-si` zero-copy
  streaming support.

---
