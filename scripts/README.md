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

**Full build + deploy** (terminal workflow — builds and signs via `dotnet publish`):

```powershell
# Auto-detect the CN=Pakko Dev certificate:
.\scripts\Deploy.ps1

# Or pass the thumbprint explicitly:
.\scripts\Deploy.ps1 -Thumbprint "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
```

This will:
1. Run `dotnet publish` with Release/x64/signed MSIX settings
2. Uninstall any existing Pakko package
3. Install the new `.msix` from `src/Archiver.App/AppPackages/`
4. Print the installed version

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

## Notes

- `PakkoDev.cer` is gitignored — never commit certificates to the repository.
- All paths in the scripts are resolved relative to `$PSScriptRoot`, so they
  work regardless of your current working directory.
- The self-signed certificate is for **local development only**. Store/release
  builds require a trusted EV certificate (see T-F10).
