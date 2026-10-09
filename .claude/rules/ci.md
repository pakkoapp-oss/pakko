---
paths:
  - ".github/**"
  - "CHANGELOG.md"
---

# CI, SonarCloud and release rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **Pin third-party GitHub Actions (`org/action@vX`) to a full commit SHA, not a mutable version
  tag** — `actions/*` (first-party GitHub actions) are exempt by convention; everything else
  (`microsoft/setup-msbuild`, `nuget/setup-nuget`, etc.) should be SHA-pinned with a `# vX.Y.Z`
  trailing comment. Found via SonarCloud S7637 (T-F136).
- **Checking current SonarCloud findings:** the dashboard
  (`sonarcloud.io/summary/overall?id=pakkoapp-oss-1_pakko&branch=main`) is a JS SPA a plain fetch
  won't render — use the public REST API instead, no auth needed for this public project:
  `sonarcloud.io/api/issues/search?componentKeys=pakkoapp-oss-1_pakko&branch=main&resolved=false&ps=100`
  (WebFetch renders it fine). Reflects the last CI-analyzed push, not uncommitted local changes —
  re-check after pushing if verifying a specific fix landed clean.
- **GitHub Actions CI (`.github/workflows/build.yml`, T-F122):**
  - `gh run view --job=<id> --log`/`--log-failed` only returns output **after the whole workflow
    run completes**, not just that one job — "run ... is still in progress" otherwise, even if the
    specific job you want logs for already finished.
  - `gh attestation verify` (and possibly other `gh` subcommands) can print nothing to stdout/
    stderr yet still exit 0 via the Bash tool — pass `--format json` for reliable output instead
    of trusting empty plain-text as a failure signal.
  - `vs_installer.exe modify --add <component>` is unreliable on GitHub-hosted Windows runners —
    confirmed it returns exit code 0 in under 30ms regardless of `--wait`/`--nocache`/running it
    twice, without actually installing anything. Don't trust it for CI component installation;
    pin a runner image that already ships what you need instead (see next point).
  - The `windows-latest` GitHub Actions runner label is not a stable OS pin — it silently moved
    from the `windows-2022` image to `windows-2025` mid-project (confirmed T-F122, 2026-07-19),
    breaking ARM64 C++ builds that worked before. Pin an explicit version (`windows-2022`) for any
    job where toolchain reproducibility matters.
  - **`gh workflow run build.yml --ref <tag>` (workflow_dispatch) is safe to run again on a tag that
    already has a push-triggered release** — `build-msix`/`build-cli`/`release` all correctly report
    `skipped` (not a conflict/failure) on a manual dispatch, since their own `if:` conditions gate
    them to the push/tag-trigger path only; just `build-store-msix`/`bundle-store-msix` actually
    run. Confirmed T-F142/v1.4.7 — no duplicate-release error, no wasted red X.
  - **`gh release download`/other repo-scoped `gh` commands need `--repo pakkoapp-oss/pakko`**
    when the Bash tool's cwd isn't inside the git repo (e.g. downloading a release artifact into
    the scratchpad for a real-artifact smoke test) — otherwise it fails with a misleading `fatal:
    not a git repository`, not a permissions/network error (T-F151/T-F153 smoke tests).
- **Cutting a public release (a `vX.Y.Z` git tag, distinct from the internal MSIX packaging
  number above):** before the `chore(release): bump to vX.Y.Z` commit, add a new section to
  `CHANGELOG.md` (newest first) listing the `T-Fxx` tasks completed since the previous tag, in
  plain language — check `docs/TASKS_DONE.md`/`git log <prev-tag>..HEAD` for what actually shipped,
  don't guess from memory. Keep it in the same commit as the version bump. `CHANGELOG.md` is the
  canonical, human-browsable release history; `.github/RELEASE_NOTES_TEMPLATE.md` stays a static
  per-release download blurb, not a task list.
  **The section header format is load-bearing, not just style (fixed 2026-08-04):** `build.yml`'s
  `release` job extracts the new tag's own `## v<tag> — <date>` section (sections split on a bare
  `---` line) and prepends it to the actual GitHub Release notes, ahead of the static template —
  before this fix, every prior release's GitHub-side notes were the template alone, with no
  changelog content at all. Get the header wrong or omit a tag's section and that release's notes
  silently fall back to template-only again.
  **Before tagging, run the nightly checks on the release commit (T-F240):** after pushing it,
  `gh workflow run canary.yml --ref main`, confirm the run's `headSha` is that commit, and read the
  verdict from `gh run view <id> --json jobs` — not the run's colour, since the masked jobs report
  success even when they failed: `canary-failed-day` must be `skipped` and `canary-fuzz` `success`.
