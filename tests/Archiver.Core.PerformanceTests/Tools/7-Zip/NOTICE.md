# Vendored 7-Zip (test-only dependency)

**This binary is never shipped in Pakko's MSIX package.** It exists solely so
`Archiver.Core.PerformanceTests` (T-F114) has a deterministic, always-present reference
implementation to compare Pakko's own ZIP compression/extraction speed against, on any machine
that checks out this repo — see `CLAUDE.md`'s "No 7-Zip" hard constraint, which governs the
*shipped product* only (`Archiver.Core`/`Archiver.App`/`Archiver.Shell`), not test infrastructure.

## Provenance

- **Project:** [7-Zip](https://www.7-zip.org/) (upstream mirror: [ip7z/7zip](https://github.com/ip7z/7zip))
- **Version:** 26.02 (released 2026-06-25)
- **Package:** `7z2602-extra.7z` ("7-Zip Extra: standalone console version")
- **Source URL:** https://github.com/ip7z/7zip/releases/download/26.02/7z2602-extra.7z
- **Package SHA-256** (verified against GitHub's published release digest before extraction):
  `081df9e9311dfd9c9e0e98c1c80180b99bb51e4cb24156b5f3057fe3c259d70a`
- **Vendored:** 2026-07-17

## Vendored files and their individual hashes

| File | Source (inside `7z2602-extra.7z`) | SHA-256 |
|---|---|---|
| `x64/7za.exe` | `x64/7za.exe` | `35d4d69d7cd6cb44558f208c3b1334268013f9daf82d2dda848893a1c30c59c2` |
| `arm64/7za.exe` | `arm64/7za.exe` | `cadbd34657713935222eb14fddbcdd51953501b44c749d9a029fab8f1c46be7e` |

Only the x64 and arm64 standalone console builds (`7za.exe`) are vendored — matching this
solution's own "x64 and ARM64 only" platform constraint (`CLAUDE.md`). The plain x86 `7za.exe`
included in the same upstream package is intentionally not vendored.

## License

`LICENSE-7-Zip.txt` in this same folder is 7-Zip's own `License.txt`, copied verbatim from the
`7z2602-extra.7z` package. 7-Zip's core code is LGPL v2.1+. Per that license, redistributing this
binary requires: stating that 7-Zip is used, stating it is LGPL-licensed, linking to
https://www.7-zip.org/, and including the license text — all satisfied by this file and
`LICENSE-7-Zip.txt`.

## Re-verifying or updating

To confirm this binary hasn't been tampered with, recompute its SHA-256 and compare against the
table above:

```powershell
Get-FileHash .\x64\7za.exe -Algorithm SHA256
```

To bump to a newer 7-Zip release: download the new version's `*-extra.7z` package, verify its
package-level SHA-256 against the digest published in that release's GitHub API metadata
(`https://api.github.com/repos/ip7z/7zip/releases/tags/<version>`) *before* extracting anything,
extract `x64/7za.exe` and `arm64/7za.exe`, replace the files in this folder, and update every
version/hash/date in this document.
