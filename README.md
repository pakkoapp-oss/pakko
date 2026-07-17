# Pakko — Windows ZIP Archiver

Minimal WinUI 3 GUI wrapper for Windows built-in ZIP support.

**No 7-Zip. No WinRAR. No third-party compression code.**

[☕ Support the project on Ko-fi](https://ko-fi.com/pakko_app)

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
| ZIP | ✅ read/write, v1.0 | `System.IO.Compression` |
| TAR/GZ/BZ2/XZ/ZST/LZMA | ✅ read (v1.3) + create (v1.4) | `tar.exe` (Windows built-in), AppContainer-sandboxed for extraction |
| RAR | ✅ read only, v1.3 | `tar.exe` (Windows built-in) — libarchive has no RAR writer |
| 7z | ✅ read only, v1.3 | `tar.exe` (Windows built-in) — libarchive has no 7z writer |
| Encrypted | ❌ out of scope | — |

---

## Windows 11 Integration

Pakko closes gaps in Windows Explorer:

- **Native context menu** — Extract Here, Extract to `<folder>`, Add to archive, Add to `X.tar`,
  Test archive (both the modern `IExplorerCommand` menu and the classic "Show more options" menu)
- **File type associations** — double-click any supported archive format opens directly into
  Pakko's Archive Browser (T-F05/T-F100), not just `.zip`
- **Archive Browser** — navigate an archive's folder structure without extracting everything
  first, extract a selection or the whole archive, then climb past the archive root into the real
  filesystem (drives, "This PC") the same way NanaZip's classic file manager does

Windows 11 23H2+ includes `tar.exe` (Microsoft-signed bsdtar), which Pakko uses — no third-party
compression tools — to read RAR/7z/tar/gz/bz2/xz/zst/lzma, and to create tar-family archives
(plain tar plus the five compression-filter variants).

---

## Project Status

**v1.1 complete** (tagged `v1.1.0`) — GitHub-only release for early testers.
**v1.2 (shell extension) complete** — native right-click context menu (`IExplorerCommand`), file
type association, and MOTW propagation.
**v1.3 (tar.exe integration) complete** — reading RAR/7z/tar-family archives via the Windows
built-in `tar.exe`, run inside an AppContainer sandbox (its own Job Object, no network capability)
for extraction; archive creation runs unsandboxed since it only ever reads trusted local files.
**v1.4 complete except Group Policy/ADMX support (T-F51, still open/future)** — Archive Browser
(browse, extract-selected/all, nested-archive drill-down, preview, climb into the real
filesystem), TAR creation via `tar.exe`, and the AppContainer sandbox are all done.

- ✅ Archive (single / separate) with compression level selector, ZIP or any tar-family format
- ✅ Extract with smart folder logic, ZIP slip protection, and a per-conflict Ask/Overwrite/
  Rename/Skip resolution
- ✅ Password-protected ZIP detection
- ✅ System tray icon
- ✅ File log (`%LocalAppData%\Pakko\logs\pakko.log`)
- ✅ i18n — 37 locales, OS-language auto-match with English fallback
- ✅ MSIX packaging, signed with a dev cert via `Deploy.ps1`
- ✅ Mid-file cancellation (async streaming)
- ✅ Safe temp file/dir pattern — no partial files on cancel
- ✅ Compression-ratio bomb detection (1000:1 threshold), confirm-and-extract if the destination
  has room, for ZIP and every tar-family format
- ✅ UTF-8 filenames — Cyrillic and emoji round-trip verified
- ✅ Native right-click context menu — Extract here, Extract to folder, Add to archive/`X.tar`,
  Test archive
- ✅ File type association (every readable format) + `pakko://` protocol activation
- ✅ MOTW propagation on every extracted file, including Archive Browser previews
- ✅ Alternate Data Stream / reserved-filename / reparse-point protections during extraction
- ✅ Archive Browser — navigate, extract selected/all, preview an image or text file without a
  manual extract, climb past the archive root into the real filesystem
- ✅ RAR/7z/tar-family extraction runs inside an AppContainer sandbox — quarantine staging, ACL'd
  output directory, Job Object process limits, no network capability
- ✅ 414/414 .NET tests pass (`dotnet test --filter "Category!=Slow"`) — a separate C++ Google
  Test suite covers the `Archiver.ShellExtension` COM DLL

Microsoft Store release planned once Group Policy/ADMX support (T-F51) is done. v1.1–v1.4 are
GitHub-only releases for early testers.

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
dotnet test --filter "Category!=Slow"
```

Always run without a path argument — all projects must stay green after every change. The
`Category!=Slow` filter excludes a handful of real multi-second/multi-GB Zip64 tests; run
`dotnet test --filter "Category=Slow"` too before a release. To regenerate test fixtures:
```bash
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```
