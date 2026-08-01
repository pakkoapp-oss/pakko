# Pakko — Developer Deployment Scripts

These scripts handle local MSIX signing and sideloading during development.
They are not part of the build pipeline — run them manually from a PowerShell terminal.

---

## Prerequisites

- Windows 10/11 with Developer Mode enabled, **or** sideloading allowed via Group Policy
- .NET 8 SDK
- Windows App SDK / WinUI 3 build tools (Visual Studio 2022 with the workload installed)

---

## Step 1 — Set up the developer certificate (once)

```powershell
.\scripts\Setup-DevCert.ps1
```

This will:
1. Relaunch itself as Administrator if needed
2. Create a self-signed `CN=Pakko Dev` code-signing certificate in `Cert:\CurrentUser\My`
3. Export it as `scripts/PakkoDev.cer` (gitignored)
4. Install it into `Cert:\LocalMachine\TrustedPeople` so Windows trusts signed packages

At the end it prints the **certificate thumbprint** — copy it for use with `Deploy.ps1`.
You only need to run this once per machine (or when the certificate expires).

---

## Step 2 — Build and install (after every change)

**Full build + deploy** (terminal workflow):

```powershell
# Auto-detect the CN=Pakko Dev certificate (x64 default):
.\scripts\Deploy.ps1

# ARM64 build:
.\scripts\Deploy.ps1 -Architecture arm64

# Or pass the thumbprint explicitly:
.\scripts\Deploy.ps1 -Thumbprint "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
```

This will:
1. Build `Archiver.Shell` (`dotnet build`, self-contained) for the target architecture
2. Build `Archiver.ShellExtension.dll` (`MSBuild.exe` directly on the `.vcxproj`, with
   `/p:SolutionDir` passed explicitly — see `DECISIONS.md` for why)
3. Run `dotnet publish` on `Archiver.App.csproj` with `GenerateAppxPackageOnBuild=true` and
   `AppxPackageSigningEnabled=true` + `PackageCertificateThumbprint=<thumbprint>` — packaging
   *and* signing happen in this one step. `Content Include` items in `Archiver.App.csproj`
   (conditioned on `GenerateAppxPackageOnBuild=true`) declare `Archiver.Shell.exe` and
   `Archiver.ShellExtension.dll` as package content, so `dotnet publish` includes them
   automatically — there is no separate `Archiver.Package.wapproj` and no manual `SignTool.exe`
   call (a manual `SignTool` call on an MSIX produces `ERROR_BAD_FORMAT`; see `DECISIONS.md`
   "MSIX Signing")
4. Uninstall any existing Pakko package
5. Install the new `.msix` from `src/Archiver.App/AppPackages/`
6. Print the installed version, then bump `Package.appxmanifest`'s version (unless
   `-SkipVersionBump`)

**`-Architecture`** — `"x64"` (default) or `"arm64"`. Derives the MSBuild Platform and runtime identifier automatically.

> There is no `Archiver.ProgressWindow` project — it was removed (see `DECISIONS.md`, T-F65).
> Shell-triggered operations show progress via the Windows Shell's built-in `IProgressDialog`,
> in-process, no second `.exe`.

**Deploy only** (skips build — installs the most recently built `.msix`):

```powershell
.\scripts\Deploy.ps1 -DeployOnly
```

> **Visual Studio post-build event** — Release builds in Visual Studio run
> `Deploy.ps1 -DeployOnly` automatically after the build completes, so no
> manual script invocation is needed when building from VS.

---

## Step 3 — Test protocol activation

After installing, verify the `pakko://` URI scheme works:

```powershell
$files = '["C:\\path\\to\\file.zip"]'
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($files))
Start-Process "pakko://extract?files=$b64"
```

Pakko should launch and begin extracting the specified archive.

---

## Publishing the standalone CLI (Archiver.CLI, T-F09)

`Publish-Cli.ps1` is **independent of everything above** — `Archiver.CLI` is never packaged into
the MSIX, needs no dev-signing certificate, and `Deploy.ps1` never touches it.

```powershell
.\scripts\Publish-Cli.ps1                    # both architectures (default)
.\scripts\Publish-Cli.ps1 -Architecture x64  # one architecture only
```

Publishes a self-contained build per architecture to `artifacts/cli/<rid>/` (gitignored) — the
built exe is `pakko.exe` (`AssemblyName`, distinct from the `Archiver.CLI` project/folder name) —
zips each as `pakko-<rid>.zip`, and writes a `SHA256SUMS` file covering both zips — ready to
attach directly to a GitHub Release. See `CLI.md`'s "Distribution" section for why no `tar.exe`
copy is bundled alongside it.

---

## Continuous Integration (T-F122)

`.github/workflows/build.yml` builds both artifacts automatically — it is a separate, CI-only
path alongside everything above, not a replacement for local `Deploy.ps1`/`Publish-Cli.ps1` use:

- **On every push to `main` and on pull requests into `main`:** runs
  `dotnet test --filter "Category!=Slow&Category!=VeryLarge"` plus the C++
  `Archiver.ShellExtension.Tests` suite. A red suite blocks every downstream job.
- **On every push to `main` (after tests pass):** builds and signs the MSIX for both `x64` and
  `arm64` via a new CI-only script, `scripts/CI-Build-Msix.ps1` (a `Deploy.ps1` sibling covering
  just the build+sign steps — no install, no version bump), and publishes `pakko.exe` for both
  architectures via the existing `Publish-Cli.ps1` unchanged. Both are uploaded as workflow
  artifacts.
- **On a version tag push (`v*`):** additionally creates a real GitHub Release for that tag and
  attaches `pakko-win-x64.zip`, `pakko-win-arm64.zip`, and `SHA256SUMS` via the `gh` CLI. This is
  now the **only** planned CLI-Release publication path — there is no separate manual publish
  step to remember. The MSIX is *not* attached to the public Release (it's still signed with a
  sideload-only self-signed cert — see below); it stays a workflow-run artifact, downloaded
  manually to hand to testers, same as today.

**Signing identity:** CI signs with the exact same local `CN=Pakko Dev` dev cert `Deploy.ps1`
uses (thumbprint `D2EC5F2C451ED0EBE94B8168A68E5B813954CC75`), exported once as a PFX and stored
as two repo secrets, `PAKKO_DEV_CERT_PFX_BASE64` and `PAKKO_DEV_CERT_PASSWORD`. See
`build.yml`'s header comment for the exact swap point once T-F10 (SignPath Foundation) issues a
real certificate — only those two secrets (and the thumbprint constant next to them) need to
change, nothing else in the workflow.

---

## Store-submission builds (`build-store-msix`, T-F129)

A separate, `workflow_dispatch`-only job in the same `build.yml`, for packages actually uploaded
to Partner Center — kept out of the automatic push/tag pipeline since a real Store submission is
a deliberate, occasional act, not something that should fire on every commit.

**Why a separate job at all:** `dotnet publish`'s MSIX packaging rewrites the built package's
`Identity/Publisher` to match whatever certificate signs it. Every package built with the
tester-facing `CN=Pakko Dev` cert above therefore ships with `Publisher="CN=Pakko Dev"` — not
Pakko's reserved Partner Center identity (`CN=EF3EC84C-8287-4FC3-BB4F-FCCEBA116BCE`) — and Partner
Center rejects it outright. Confirmed against a real submission attempt, 2026-08-01: both
`Invalid package publisher name` and the derived `Invalid package family name` errors clear once
signed with the correct-Subject cert. See `docs/DECISIONS.md`'s T-F129 entry for the full trail.

**Run it:** Actions tab → Build → Run workflow, against the tag/ref you're about to submit. Two
architecture legs (`x64`, `arm64`) build+sign on `windows-2022` (`build-store-msix`, same
ARM64-v143-toolset reasoning as `build-msix`) as single-architecture `.msixbundle`s, then a third
job (`bundle-store-msix`) unbundles both, merges them into **one** real multi-architecture
`.msixbundle`, and re-signs it — download that single `pakko-store-msixbundle` artifact from the
run's Summary page and upload it to Partner Center's Packages step. **Not** attached to the public
GitHub Release: a differently-signed package with a different `Publisher` would be confusing for
end users to stumble onto, and Microsoft re-signs for real distribution anyway once certification
passes, so the local signature here only needs to make packaging/upload succeed.

**Why the merge step exists:** a `.msixbundle`'s own `Identity` carries no `ProcessorArchitecture`
attribute — only the packages *inside* it do. Two separately-built single-architecture bundles
(one containing only the x64 `.msix`, one containing only the arm64 `.msix`) therefore compute to
the exact same full package name (`..._Neutral_...`) despite having different contents byte-for-
byte, and Partner Center rejects the upload outright: "All .msix and .appx packages ... must be
uniquely identified by their full names." Confirmed against a real submission, 2026-08-01. The fix
is `makeappx unbundle` on each, `makeappx bundle /bv <version>` on the combined inner `.msix`
files, then `signtool sign` again (bundling strips the original per-bundle signature) — this is
also Microsoft's own documented shape for a multi-architecture Store app, not a workaround.

**Local machines are not the reference for this build.** `scripts/Setup-StoreCert.ps1` exists so a
developer *can* build and smoke-test a Store-identity package locally (useful for verifying the
fix before relying on CI, or if the cert ever needs rotating), but the artifact actually uploaded
to Partner Center should always come from this CI job, not a local `Deploy.ps1` run — confirmed
2026-08-01 that a real dev machine may simply be missing the ARM64 v143 C++ toolset MSVC needs
(a genuine, not-uncommon local environment gap; `windows-2022` runners have it out of the box).

**Signing identity:** a second self-signed cert, Subject `CN=EF3EC84C-8287-4FC3-BB4F-FCCEBA116BCE`
(thumbprint `CD8DE1646CBF5A52046001FB32B0B60B797E7497`), created via `Setup-StoreCert.ps1` and
exported once as a PFX into two more repo secrets, `PAKKO_STORE_CERT_PFX_BASE64` and
`PAKKO_STORE_CERT_PASSWORD` — same pattern as the dev cert pair, just a different Subject/secret
names. This is also a self-signed cert, not a "real" purchased one: Partner Center accepts
self-signed-cert-signed uploads routinely, since Microsoft re-signs on successful certification —
the Subject matching the reserved Publisher is what actually matters here, not the cert's issuer.

---

## SonarCloud static analysis (T-F135)

The `test` job runs a SonarCloud scan wrapped around the existing `dotnet test` invocation (JDK
setup → `dotnet-sonarscanner begin` → `dotnet test` → `dotnet-sonarscanner end`), on every push to
`main`/tags and on same-repo pull requests. Free tier — SonarCloud analysis is free for genuinely
public repositories, which this one is. Fork-originated PRs skip the Sonar steps entirely (no
`SONAR_TOKEN` secret available to them) rather than failing.

**Real organization/project keys (confirmed via SonarCloud's public API, 2026-07-27) — note the
`-1` suffix on the org key, since the plain `pakkoapp-oss` key was already taken on sonarcloud.io
when the project was created, so it does NOT match the GitHub org/account name:**
- Organization key: `pakkoapp-oss-1`
- Project key: `pakkoapp-oss-1_pakko`

Both are wired into `build.yml`'s `SONAR_PROJECT_KEY`/`SONAR_ORGANIZATION` env vars and into
`README.md`'s badge — don't reuse `pakkoapp-oss`/`pakkoapp-oss_pakko` (an earlier guessed
placeholder that turned out wrong) anywhere new.

**Coverage:** the org's default "Sonar way" quality gate requires new code coverage ≥ 80% — with
no coverage data submitted, that condition reads as "no data" and fails the gate outright. The
`test` job's `dotnet test` step therefore also passes `--collect:"XPlat Code Coverage"` (using the
`coverlet.collector` package every test project already references — no new dependency), and the
`dotnet-sonarscanner begin` step passes
`/d:sonar.cs.cobertura.reportsPaths="**/coverage.cobertura.xml"` to pick up the resulting
per-test-project Cobertura reports. This makes the condition evaluate against a real number — it
does not guarantee the actual number clears 80%; if the real coverage on new code comes in lower,
the gate can still fail, and that's a legitimate signal to act on (write more tests), not a CI bug.

**One-time setup (already done 2026-07-27 — kept here for reference / re-setup):**
1. Sign in to [sonarcloud.io](https://sonarcloud.io), create the org, import the
   `pakkoapp-oss/pakko` repository as a new project (done manually, without GitHub-App binding,
   since the importing account had no Admin on the repo — see `DECISIONS.md`'s T-F135 entry).
2. Generate an analysis token (My Account → Security) and add it as a repo secret named
   `SONAR_TOKEN` (Settings → Secrets and variables → Actions) — done.
3. Analysis Method should already be "CI-based" — the manual/"Other CI" `dotnet-sonarscanner`
   setup snippet SonarCloud generated (see below) only exists for CI-based projects; Automatic
   Analysis needs the GitHub App installed with repo Admin, which was never available for this
   import (step 1). Not independently eyeballed on the Administration → Analysis Method page, but
   this project structurally couldn't have ended up in Automatic mode.
4. Once a real scan has run, confirm the quality-gate results against the actual SonarCloud
   project dashboard (https://sonarcloud.io/project/overview?id=pakkoapp-oss-1_pakko), not just a
   green GitHub Actions run — same rule this project already applies to every other CI change
   (T-F122/T-F125 precedent).

The C++ `Archiver.ShellExtension`/`Archiver.ShellExtension.Tests` projects are **not** covered by
this scan (SonarCloud's C/C++ analysis needs a separate `build-wrapper` tool and has its own
licensing terms — not yet evaluated for the free tier). Only the .NET projects are analyzed for
now.

---

## Notes

- `PakkoDev.cer` and `PakkoStore.cer` are gitignored — never commit certificates to the repository.
- All paths in the scripts are resolved relative to `$PSScriptRoot`, so they
  work regardless of your current working directory.
- The self-signed certificate is for **local development only**. Store/release
  builds require a trusted EV certificate (see T-F10).
