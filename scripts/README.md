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

## Notes

- `PakkoDev.cer` is gitignored — never commit certificates to the repository.
- All paths in the scripts are resolved relative to `$PSScriptRoot`, so they
  work regardless of your current working directory.
- The self-signed certificate is for **local development only**. Store/release
  builds require a trusted EV certificate (see T-F10).
