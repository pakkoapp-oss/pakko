---
paths:
  - "docs/**"
  - "*.md"
---

# Documentation rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

## Documentation Map

| File | Purpose | Read when | Update when |
|---|---|---|---|
| **CLAUDE.md** (here) | Session context, hard constraints, build commands, this map | Every session (auto-loaded) | Project status changes, a hard constraint changes, build/deploy commands change |
| `docs/TASKS.md` | Active/future task backlog, acceptance criteria, `T-Fxx` numbering | Starting any implementation task | A task starts/completes/changes scope; a new `T-Fxx` is claimed |
| `docs/TASKS_DONE.md` | Archive of completed v1.0 tasks | Need historical task detail | Never — append-only via tasks graduating out of `docs/TASKS.md` |
| `docs/ARCHITECTURE.md` | Current C# layer diagram + signatures + DI wiring/startup | Before writing code that touches a public signature or a DI-registered service | A public signature/model/interface in `Archiver.Core` changes, or DI registration/lifetime changes |
| `docs/XAML.md` | Current `MainWindow.xaml` structure + WinUI 3 gotchas | Touching `Archiver.App`'s XAML | XAML structure changes, a new WinUI 3 constraint is discovered |
| `docs/CONVENTIONS.md` | Coding style, naming, async, error-handling, per-project package whitelist | Before writing any code | A new convention is adopted, or a code example goes stale |
| `SECURITY.md` | Threat model — **canonical owner of all security/CVE/supply-chain/MOTW rationale** | Modifying compression, traversal, or extraction logic | Threat model changes, a new mitigation is added |
| `docs/DECISIONS.md` | Architectural decisions + rejected approaches, with root-cause detail | Before implementing packaging, COM, or shell integration | An approach is chosen, rejected, or corrected |
| `docs/DIAGRAMS.md` | Required sequence/state/activity/component diagrams, Ground Truth Rule | Touching COM/shell, operation lifecycle, `ZipArchiveService` branching, or the manifest | Per its own DoD table — same commit as the code |
| `docs/TESTING.md` | Test plan and fixture inventory for `Archiver.Core` | Writing or running tests | New test category, fixture, or test count changes |
| `tests/Archiver.Core.Tests.GenerateFixtures/README.md` | Fixture-generation mechanics only (subordinate to `docs/TESTING.md`) | Adding a fixture-dependent test | A new fixture scenario is added |
| `docs/SPEC.md` | Product specification — **canonical owner of the version roadmap table, feature scope, non-goals** | Scoping a new feature, checking what's out of scope | Scope or roadmap changes |
| `docs/CLI.md` | **Canonical owner of Archiver.CLI's (T-F09) command/switch specification** — 7z→Pakko command table, switch fidelity, three-way unknown-input rule | Implementing or extending T-F09 | The planned CLI command/switch surface changes |
| `docs/POLICIES.md` | Group Policy/ADMX admin reference (T-F51) | Touching GPO-controlled behavior | GPO-controlled behavior changes |
| `docs/SIGNING.md` | Code Signing Policy (team roles, build process, artifacts covered) — published for SignPath Foundation eligibility (T-F124) | Touching signing/release process | Signing process or team roles change |
| `README.md` | Public GitHub landing page | User-facing — not an agent instruction source | Public messaging changes; must link to `SECURITY.md`/`docs/SPEC.md`, never restate their tables |
| `CONTRIBUTING.md` | Contributor onboarding summary | Before a contributor's first build | Build/deploy steps change — update `scripts/README.md` first, then sync the summary here |
| `scripts/README.md` | **Canonical owner of build/sign/deploy steps** (`Deploy.ps1`, `Setup-DevCert.ps1`) | Running or changing the deploy scripts | `Deploy.ps1`/`Setup-DevCert.ps1` behavior changes |
| `CHANGELOG.md` | **Canonical owner of per-release history** — one section per version tag, plain-language summary of the `T-Fxx` tasks shipped since the previous tag | Cutting a release | Every version tag — see this file's "Deployment" section |
| `docs/index.html` + `docs/uk/index.html` | Public project website — bilingual EN/UK landing page: trust model, what's implemented, download links. **Deployment changed T-F172 (2026-08-13):** GitHub Pages is no longer served directly from the `/docs` branch path; `.github/workflows/build.yml`'s `docs`/`deploy-pages` jobs assemble these files (copied verbatim via an explicit allowlist) plus the DocFX site into one Pages artifact on every push to `main` — content and authoring are unchanged, only the delivery mechanism | User-facing — not an agent instruction source | Supported-format list changes, a major feature ships, download/release mechanics change, or roadmap/version-status changes — keep both language versions in sync with each other and with `README.md`'s "Project Status"/"Supported Formats" |
| `docfx.json` + `toc.yml` + `index.md` + `api/index.md` (repo root) | DocFX config for the generated developer/API docs site (T-F172) — book content is the *existing* curated `docs/*.md`/root `*.md` files read in place (no duplication), API reference is generated from `Archiver.Core`/`Archiver.App.Core`'s own XML `///` comments. Live at `https://pakkoapp-oss.github.io/pakko/dev/` | Adding a new conceptual doc that should appear in the site's nav, or a new class library whose XML comments should be included in the API reference | The curated article list changes, or a new project's API should be included — remember to add its `.csproj` to `docfx.json`'s `metadata[0].src.files` too |
| `tests/Archiver.Messages.Tests/Glossary.tsv` | The word each of 37 locales uses for ten concepts, and the words it replaced (T-F329); test data for `GlossaryTests` | Writing or changing any translated string | A term is chosen or replaced — same commit as the strings |
| `docs/store-listing/<locale>.txt` + `README.md` | The Microsoft Store listing text in 37 languages (T-F331): description, short description, features, search terms, "What's new"; the README has the field limits and the rules | Changing what the listing says, or cutting a release | A listed feature changes, a menu item is renamed, or a release needs new "What's new" lines — `en-US.txt` first, then every locale |
| `.github/workflows/canary.yml` | Nightly canary on floating toolchain versions (T-F187) plus the checks too slow for every push (T-F240: `Category=Slow` without the 7za timing ratios, ARM64 tests, C++ tests under ASan) — 3-day-streak escalation to a tracking GitHub Issue; `canary-fuzz` is outside it and fails red at once | Investigating a canary failure or its tracking Issue | Escalation logic, schedule, or build scope changes |

### Update Cascades

Some changes ripple beyond their primary doc. After updating the primary doc for a change below,
check whether the cascade docs still agree with it — don't let them silently drift (this is how
the `com:InProcessServer`/`com:SurrogateServer` drift and the `ARCHITECTURE.md`/`BOOTSTRAP.md`
DI duplication happened).

| Change | Primary doc | Cascade — check these too |
|---|---|---|
| Public signature/model change in `Archiver.Core` | `docs/ARCHITECTURE.md` | `docs/CONVENTIONS.md` (XML-doc example), `docs/TASKS.md` (mark task done) |
| DI registration or lifetime change | `docs/ARCHITECTURE.md` | — (single owner now, no cascade) |
| `MainWindow.xaml` structure or new WinUI 3 gotcha | `docs/XAML.md` | — (leaf doc) |
| New coding convention adopted | `docs/CONVENTIONS.md` | — |
| Threat model or mitigation changes | `SECURITY.md` | `docs/SPEC.md` (teaser), `README.md` (teaser) |
| Approach chosen/rejected/corrected (COM, packaging, shell) | `docs/DECISIONS.md` | `docs/ARCHITECTURE.md`, `CLAUDE.md` (hard constraints), `scripts/README.md`, `docs/DIAGRAMS.md` |
| Task starts/completes, or a new `T-Fxx` is claimed | `docs/TASKS.md` | `docs/TASKS_DONE.md` (graduation on completion), `CLAUDE.md` (Current State), `README.md` (Project Status) |
| Version scope/roadmap changes | `docs/SPEC.md` | `CLAUDE.md` (Roadmap Summary), `README.md` (Roadmap) |
| Supported-format list or a major feature ships/changes | `README.md` (Supported Formats / Project Status) | `docs/index.html` + `docs/uk/index.html` (What's Implemented section, kept identical in substance across both languages) |
| `Deploy.ps1`/`Setup-DevCert.ps1` behavior changes | `scripts/README.md` | `CONTRIBUTING.md`, `README.md` (Building and Deploying), `CLAUDE.md` (Build Commands) |
| A release is tagged (`vX.Y.Z`) | `CHANGELOG.md` | — (single owner, see "Deployment") |
| COM/shell, operation lifecycle, `ZipArchiveService` branching, or manifest changes | `docs/DIAGRAMS.md` | Per its own DoD table |
| New test or fixture added | `docs/TESTING.md` | `tests/Archiver.Core.Tests.GenerateFixtures/README.md`, `CONTRIBUTING.md` |
| New project added to `src/` or `tests/` | `docs/ARCHITECTURE.md` (folder tree) | `CONTRIBUTING.md` (Project structure table) |
| A root `.md` file is added, removed, or moved | `CLAUDE.md` (Documentation Map + Repo Layout) | Re-run the dangling-link grep below |

Before deleting or merging any `.md` file, grep the whole repo for its filename first — dead
references are easy to miss otherwise (this session found 5 lingering mentions of `AGENT.md`/
`BOOTSTRAP.md` after removing them).

**Dangling-link grep after moving/renaming any `.md` file (T-F126):** markdown-link syntax only —
`rg '\]\([A-Za-z0-9_./-]*\.md[^)]*\)' --glob '*.md'` from repo root. Real cross-references show up
in non-obvious places beyond the doc itself: `.github/*_TEMPLATE.md`, `deploy/README.md`,
`scripts/*.ps1` comments — grep those separately for bare filename mentions too, not just `.md`
files.

- **`docs/TASKS.md`'s task-graduation edits** (moving completed entries to `docs/TASKS_DONE.md`) tend to
  land in large diff hunks that intermingle several unrelated tasks — `git add -p` can't
  cleanly split one task's doc update out of such a hunk. When committing narrowly, stage
  specific files/whole hunks deliberately, or commit the doc consolidation separately.
- **Editing unicode-heavy docs (`docs/DIAGRAMS.md` mermaid blocks, `docs/DECISIONS.md`) with the Edit
  tool:** a multi-line `old_string` spanning several em-dash (—)/arrow (→) characters can
  silently fail to match even though `Read` shows it verbatim. Split into smaller edits
  (isolate one such character per edit) to work around it.
- **`docs/DIAGRAMS.md` mermaid blocks are never auto-validated — nothing in this repo's workflow
  renders them.** After editing, run each block through `npx @mermaid-js/mermaid-cli` (`mmdc -i
  diagram.mmd -o diagram.svg`) before considering the edit done. A bare `;` or an unescaped
  `"quoted phrase"` inside unquoted label/message/transition text breaks the parser in
  sequence/state/flowchart diagrams alike — use `—` instead of `;`, and quote the whole label if
  it needs literal parentheses or quotes.
