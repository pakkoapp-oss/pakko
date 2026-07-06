# Pakko — Windows ZIP Archiver

Minimal WinUI 3 GUI wrapper for Windows built-in ZIP support.

**No 7-Zip. No WinRAR. No third-party compression code.**

---

## Why Not 7-Zip or WinRAR?

Pakko uses a different trust model — not a claim of absolute security superiority, but a different
set of supply chain dependencies with different auditability properties. Both tools have supply
chain characteristics (developer jurisdiction, no reproducible builds, CVE history) that some
security-conscious environments find unacceptable.

**For the full CVE tables and rationale, see `SECURITY.md`** (the canonical source).

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
- **Mark of the Web (MOTW) propagation** — extracted files inherit `Zone.Identifier` from the archive by default; prevents macro execution in extracted Office docs (Explorer does not propagate MOTW)
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

## Windows 11 Integration

Pakko closes gaps in Windows Explorer:

- **Native context menu** — Extract Here, Extract to `<folder>`, Add to archive (no "Show more
  options" — uses the modern `IExplorerCommand` API)
- **File type associations** — double-click `.zip` opens in Pakko

Still planned: RAR/7z/tar reading via `tar.exe` (v1.3). Windows 11 23H2+ includes `tar.exe`
(Microsoft-signed bsdtar) supporting RAR, 7z, tar, gz, xz, and zst for reading. Pakko will use
this built-in binary — no third-party compression tools.

---

## Project Status

**v1.1 complete** (tagged `v1.1.0`) — GitHub-only release for early testers.
**v1.2 (shell extension) in progress** — native right-click context menu (`IExplorerCommand`),
file type association, and MOTW propagation are complete; hash viewer is still future.

- ✅ Archive (single / separate) with compression level selector
- ✅ Extract with smart folder logic and ZIP slip protection
- ✅ Password-protected ZIP detection
- ✅ System tray icon
- ✅ File log (`%LocalAppData%\Pakko\logs\pakko.log`)
- ✅ i18n foundation (ResW, en-US)
- ✅ MSIX packaging, signed with a dev cert via `Deploy.ps1`
- ✅ Mid-file cancellation (async streaming)
- ✅ Safe temp file/dir pattern — no partial files on cancel
- ✅ ZIP bomb detection (compression ratio 1000:1 threshold)
- ✅ UTF-8 filenames — Cyrillic and emoji round-trip verified
- ✅ Native right-click context menu — Extract here, Extract to folder, Add to archive
- ✅ `.zip` file type association, `pakko://` protocol activation
- ✅ MOTW propagation on every extracted file
- ✅ Alternate Data Stream / reserved-filename / reparse-point protections during extraction
- ✅ 95/95 .NET tests pass (`dotnet test`) — a separate C++ Google Test suite covers the
  `Archiver.ShellExtension` COM DLL

Microsoft Store release planned for **v1.3** — when `tar.exe` integration is complete. v1.1 and
v1.2 are GitHub-only releases for early testers.

See `SPEC.md`'s "Future Roadmap" section for the version-to-focus table, and `TASKS.md` for the
detailed task list.

---

## Building and Deploying

Prerequisites: Visual Studio 2022 (Windows App SDK / WinUI 3 + Desktop C++ workloads), .NET 8 SDK.

```powershell
.\scripts\Setup-DevCert.ps1   # once per machine
.\scripts\Deploy.ps1          # build, sign, and sideload the MSIX
```

See [`scripts/README.md`](scripts/README.md) for full details and [`CONTRIBUTING.md`](CONTRIBUTING.md)
for the contributor workflow. Production code signing with a trusted certificate is planned (T-F10).

---

## Running Tests

```bash
dotnet test
```

Always run without a path argument — all projects must stay green after every change. To
regenerate test fixtures:
```bash
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```
