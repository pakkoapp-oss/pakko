# Windows Archiver Wrapper

Minimal WinUI 3 GUI wrapper for Windows built-in ZIP support.

**No 7-Zip. No WinRAR. No third-party compression code.**

---

## Why Not 7-Zip or WinRAR?

Both tools are widely used but carry a supply chain risk that is unacceptable for security-conscious environments.

**7-Zip** is developed and maintained by a single Russian developer (Igor Pavlov).
The source code is published, but:
- no reproducible builds — you cannot verify the distributed `.exe` matches the source
- no independent security audit of the codebase
- single point of trust with no transparent governance or code review process

**WinRAR** is developed by Eugene Roshal, also Russian. It is closed-source — independent audit is impossible by design.

Both tools have a documented history of critical vulnerabilities: path traversal on extraction, buffer overflows in archive parsers, and ACE format exploits (CVE-2018-20250 in WinRAR, multiple CVEs in 7-Zip).

For government agencies, defense organizations, critical infrastructure, and businesses handling sensitive data, the combination of opaque ownership and unverifiable binaries is an unacceptable risk.

---

## What This Project Uses Instead

| Component | Source | Auditability |
|-----------|--------|-------------|
| ZIP compression | `System.IO.Compression` — Microsoft .NET BCL | Open source, part of .NET runtime |
| UI framework | WinUI 3 / Windows App SDK — Microsoft | Open source on GitHub |
| No other compression code | — | — |

The entire compression stack is part of the .NET Base Class Library, maintained by Microsoft with a public CVE process, reproducible builds, and community audit via the `dotnet/runtime` GitHub repository.

---

## Security Properties

- **No third-party compression dependencies** — attack surface limited to .NET runtime
- **Open source** — full codebase auditable by anyone
- **Reproducible builds** — MSIX package can be verified against source *(planned)*
- **Minimal permissions** — no network access, no background services, no shell extensions in v1.0
- **No telemetry** — no data leaves the machine

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | WinUI 3 + Windows App SDK |
| Language | C# 12 / .NET 8 LTS |
| Compression | `System.IO.Compression` (ZIP) |
| Distribution | Microsoft Store (MSIX) + GitHub releases |
| License | Apache 2.0 |

---

## Supported Formats

| Format | Status |
|--------|--------|
| ZIP | ✅ v1.0 |
| TAR | 🔜 planned (via Windows built-in `tar.exe`) |
| RAR, 7z, encrypted | ❌ out of scope by design |

---

## Project Status

Work in progress. See `TASKS.md` for implementation status.
