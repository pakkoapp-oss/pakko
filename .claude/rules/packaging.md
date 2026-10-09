---
paths:
  - "src/Archiver.App/Package.appxmanifest"
  - "src/**/*.csproj"
  - "scripts/Deploy.ps1"
  - "scripts/CI-Build-Msix.ps1"
---

# MSIX packaging rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **MSIX packaging:** never use `BeforeTargets` hooks or manual `MakeAppx` calls to inject files
  into packages. Use `Content Include` items in `.csproj` with `CopyToOutputDirectory` — this is
  the only reliable approach that survives incremental builds. `dotnet publish` with
  `AppxPackageSigningEnabled=true` is the only confirmed working signing method; manual
  `SignTool` calls fail on MSIX because `New-SelfSignedCertificate` generates CNG keys on modern
  Windows and SignTool cannot use CNG keys to sign MSIX directly.
- **25+ locale resource packages force a `.msixbundle`, not a flat `.msix`** (found 2026-07-07
  right after T-F91 added 24 locale folders, taking the app from 1 to 25 total resource
  packages). MSBuild's packaging pipeline needs a bundle once there are enough per-language
  resource packages that the device must selectively install a subset — a flat `.msix` can't
  hold multiple resource-qualified sub-packages. `Deploy.ps1`'s "locate the final package" step
  only searched `-Filter '*.msix'`, so it silently found nothing and failed with `No .msix file
  found under ...AppPackages` even though `dotnet publish` had already succeeded and produced a
  real `.msixbundle`. Fixed by widening that search to `-Include '*.msix', '*.msixbundle'` —
  `Add-AppxPackage` installs either directly. If you ever reduce the shipped locale count back
  down, expect the output to flip back to a flat `.msix` — both are handled now.
## Windows Packaging Best Practices

Root-cause detail for the first six points below lives in `docs/DECISIONS.md` ("MSIX Satellite EXE
Packaging", "MSIX Signing", "Context Menu Appeared But Commands Did Nothing") — this is the
quick-reference list only, to avoid known failure modes without re-reading the full postmortems:

- Satellite EXEs: `Content Include` in `Archiver.App.csproj`
  (`Condition="'$(GenerateAppxPackageOnBuild)'=='true'"`), never `BeforeTargets`/manual `MakeAppx`
- MSIX signing: `AppxPackageSigningEnabled=true` + `PackageCertificateThumbprint` in
  `dotnet publish`, never manual `SignTool` (`ERROR_BAD_FORMAT` on MSIX)
- Self-signed certs: pass `-Provider "Microsoft Strong Cryptographic Provider"` to
  `New-SelfSignedCertificate` (default CNG keys break SignTool)
- Never use `.wapproj` with multiple WinUI 3 apps (duplicate `Files/App.xbf` PRI entries)
- Every EXE launched via `CreateProcess` from outside its own package needs its own
  `<Application>` entry in `Package.appxmanifest` (`EntryPoint="Windows.FullTrustApplication"`,
  `AppListEntry="none"` to hide it) — otherwise `ERROR_ACCESS_DENIED`
- Satellite EXEs ship as one Native AOT exe each (T-F355), packaged via `Content Include` of the `.exe` alone

Two more, not duplicated elsewhere:

- **A hidden satellite `<Application>` (`AppListEntry="none"`, e.g. `Archiver.Shell.exe`'s entry)
  triggers a Store "headless app" rejection.** Requires a separate account-level
  `HeadlessAppBypass` waiver request from Microsoft — not a manifest fix, since removing
  `AppListEntry="none"` would break the intended hidden-process UX. Budget real calendar time for
  Microsoft's response before assuming a Store submission is close to done.
- **`Package.appxmanifest`'s `Version` revision (4th segment) must be `0` at Store submission** —
  the tracked file always is (T-F368, `PackagingManifestTests`); never commit a nonzero one.
- **`src/Archiver.App/Assets/pakko-icon.svg` is the canonical vector source for every brand-mark
  asset** (Square44x44/150x150Logo, Wide310x150Logo, SplashScreen, StoreLogo). Regenerate raster
  assets from this SVG's real geometry, never by upscaling an existing `.png` — confirmed via a
  real regression this session (upscaling silently lost rounded corners present in the true
  original, caught only by checking `git show HEAD:<path>` pixel values, not by eyeballing output).
- **A satellite project's `TargetFramework` is embedded literally in other projects' `Content
  Include` paths and in `Deploy.ps1`.** Bumping `Archiver.Shell.csproj`'s TFM (e.g. to
  `net8.0-windows10.0.17763.0` for WinRT APIs) silently moved its real build output to a new
  folder, but `Archiver.App.csproj`'s four `Content Include` items and `Deploy.ps1`'s
  `$shellExeSourcePath` kept pointing at the old TFM segment — `Deploy.ps1` kept reporting
  "installed successfully" with a fresh version number and a fresh `.exe` apphost timestamp while
  silently installing a stale managed `.dll`. Caught only by comparing the `.dll`'s file *size*,
  not the `.exe`'s timestamp (the apphost stub barely changes across builds). Grep every
  `net10.0-windows`-style TFM literal across `.csproj`/`.ps1` files before changing any project's
  TFM, not just the one project's own file (T-F128).
