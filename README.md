# Pakko — Windows ZIP Archiver

Minimal WinUI 3 GUI wrapper for Windows built-in ZIP support.

**No 7-Zip. No WinRAR. No third-party compression code.**

---

## Why Not 7-Zip or WinRAR?

Both tools carry supply chain risk unacceptable for security-conscious environments.

**7-Zip** — developed by a single Russian developer (Igor Pavlov). Source published but no reproducible builds — the distributed binary cannot be verified against source. No independent security audit. Multiple critical CVEs.

**WinRAR** — developed by Eugene Roshal (Russia). Closed source — independent audit impossible. CVE-2018-20250 affected 500M+ installations.

For government, defense, and critical infrastructure: unverifiable binaries from developers in adversarial jurisdictions processing sensitive documents is an unacceptable supply chain risk.

---

## What Pakko Uses Instead

| Component | Source | Auditability |
|-----------|--------|-------------|
| ZIP compression | `System.IO.Compression` — .NET BCL | Open source, part of .NET runtime |
| UI framework | WinUI 3 / Windows App SDK | Open source on GitHub |

The entire compression stack is part of the .NET Base Class Library — maintained by Microsoft with a public CVE process, reproducible builds, and community audit via `dotnet/runtime`.

---

## Security Properties

- **No third-party compression dependencies** — attack surface limited to .NET runtime
- **Open source** — full codebase auditable
- **SHA-256 integrity manifest** — archives created by Pakko carry a `PAKKO-INTEGRITY-V1` manifest in the ZIP comment; verified on extraction
- **Minimal permissions** — no network access, no background services, no shell extensions
- **No telemetry** — no data leaves the machine

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | WinUI 3 + Windows App SDK |
| Language | C# 12 / .NET 8 LTS |
| Compression | `System.IO.Compression` (ZIP) |
| Distribution | MSIX (self-contained) |
| Min OS | Windows 10 1809 (build 17763) / Windows Server 2019 |
| License | Apache 2.0 |

---

## Supported Formats

| Format | Status |
|--------|--------|
| ZIP | ✅ v1.0 |
| TAR | 🔜 planned (via Windows built-in `tar.exe`) |
| RAR, 7z | ❌ out of scope by design |
| Encrypted | ❌ out of scope by design |

---

## Project Status

**v1.0 complete** — tagged `v1.0.0`.

- ✅ Archive (single / separate) with compression level selector
- ✅ Extract with smart folder logic and ZIP slip protection
- ✅ SHA-256 integrity manifest (PAKKO-INTEGRITY-V1)
- ✅ Password-protected ZIP detection
- ✅ System tray icon
- ✅ File log (`%LocalAppData%\Pakko\logs\pakko.log`)
- ✅ i18n foundation (ResW, en-US)
- ✅ MSIX packaging
- ✅ 45 tests

See `TASKS.md` for future roadmap.

---

## Building MSIX

Prerequisites: Windows 10 SDK (`makeappx.exe`), .NET 8 SDK

```
dotnet publish src/Archiver.App/Archiver.App.csproj ^
    /p:Configuration=Release /p:Platform=x64 ^
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true ^
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false
```

Output: `src/Archiver.App/AppPackages/Archiver.App_1.0.0.0_x64_Test/Archiver.App_1.0.0.0_x64.msix`

The package is unsigned in v1.0. To install locally:
- Enable Developer Mode in Windows Settings, then use `Add-AppxPackage` with a self-signed cert
- Or sign with a self-signed cert: `New-SelfSignedCertificate` + `signtool.exe`

Code signing with a trusted certificate is planned (T-F10).

---

## Running Tests

```bash
dotnet test tests/Archiver.Core.Tests
```

To regenerate test fixtures:
```bash
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```
