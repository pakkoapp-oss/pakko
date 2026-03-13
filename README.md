# Pakko — Windows ZIP Archiver

Minimal WinUI 3 GUI wrapper for Windows built-in ZIP support.

**No 7-Zip. No WinRAR. No third-party compression code.**

---

## Why Not 7-Zip or WinRAR?

Pakko uses a different trust model — not a claim of absolute security superiority, but a different set of supply chain dependencies with different auditability properties. Both tools have supply chain characteristics that some security-conscious environments find unacceptable.

**7-Zip** — source published but no reproducible builds — the distributed binary cannot be verified against source. No independent security audit. Multiple critical CVEs (CVE-2016-9296, CVE-2017-17969, CVE-2018-10115, CVE-2022-29072).

**WinRAR** — closed source — independent audit impossible. CVE-2018-20250 (path traversal, 500M+ installations affected).

For government, defense, and critical infrastructure: organizations that require auditability of every binary processing sensitive files may prefer a tool whose entire compression stack is open source, reproducible, and maintained via a public CVE process. This is a trust model choice, not a guarantee of zero vulnerabilities.

---

## What Pakko Uses Instead

| Component | Source | Auditability |
|-----------|--------|-------------|
| ZIP compression | `System.IO.Compression` — .NET BCL | Open source, part of .NET runtime |
| UI framework | WinUI 3 / Windows App SDK | Open source on GitHub |

The entire compression stack is part of the .NET Base Class Library — maintained by Microsoft with a public CVE process, reproducible builds, and community audit via `dotnet/runtime`.

> **Trust dependency:** The .NET runtime and Windows App SDK are themselves trust dependencies. Pakko's security properties depend on the integrity of Microsoft's supply chain and build infrastructure. Organizations that trust the Microsoft/.NET ecosystem will find this architecture auditable; those that do not should evaluate accordingly.

---

## Security Properties

- **No third-party compression dependencies** — attack surface limited to .NET runtime
- **Open source** — full codebase auditable
- **Minimal permissions** — no network access, no background services
- **No telemetry** — no data leaves the machine
- **Mark of the Web (MOTW) propagation** — planned v1.2; prevents macro execution in extracted Office docs (Explorer does not propagate MOTW)
- **No libarchive in-process** — tar/RAR/7z extraction via isolated `tar.exe` subprocess, not an in-process parser

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

| Format | Status | Method |
|--------|--------|--------|
| ZIP | ✅ v1.0 | `System.IO.Compression` |
| TAR/GZ/XZ/ZST/BZ2 | 🔜 v1.3 | `tar.exe` (Windows built-in) |
| RAR | 🔜 v1.3 (read) | `tar.exe` (Windows built-in) |
| 7z | 🔜 v1.3 (read) | `tar.exe` (Windows built-in) |
| Encrypted | ❌ out of scope | — |

---

## Windows 11 Integration (planned)

Pakko will close the remaining gaps in Windows Explorer:

- **Native context menu** — Extract Here, Extract to `<folder>`, Archive with Pakko (no "Show more options" — uses modern IExplorerCommand API)
- **File type associations** — double-click `.zip` opens in Pakko
- **Extract-on-open** — optional auto-extract on file open

Windows 11 23H2+ includes `tar.exe` (Microsoft-signed bsdtar) supporting RAR, 7z, tar, gz, xz, and zst for reading. Pakko will use this built-in binary — no third-party compression tools.

---

## Project Status

**v1.1 complete — GitHub release** for early testers. v1.0 tagged `v1.0.0`, v1.1 tagged `v1.1.0`.

- ✅ Archive (single / separate) with compression level selector
- ✅ Extract with smart folder logic and ZIP slip protection
- ✅ Password-protected ZIP detection
- ✅ System tray icon
- ✅ File log (`%LocalAppData%\Pakko\logs\pakko.log`)
- ✅ i18n foundation (ResW, en-US)
- ✅ MSIX packaging
- ✅ Mid-file cancellation (async streaming)
- ✅ Safe temp file/dir pattern — no partial files on cancel
- ✅ ZIP bomb detection (compression ratio 1000:1 threshold)
- ✅ UTF-8 filenames — Cyrillic and emoji round-trip verified
- ✅ 48 tests

Microsoft Store release planned for **v1.3** — when shell extension and MOTW propagation are complete. v1.1 and v1.2 are GitHub-only releases for early testers.

**Roadmap:**

| Version | Focus |
|---------|-------|
| v1.1 | ✅ GitHub release — ZIP only |
| v1.2 | Shell extension + MOTW + file associations + hash viewer |
| v1.3 | tar.exe integration + Microsoft Store release |
| v1.4 | GPO/ADMX + Low IL sandbox (P/Invoke) + strict mode policy |
| v1.5 | TAR creation via tar.exe + additional format fixtures |

See `TASKS.md` for detailed task list.

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
