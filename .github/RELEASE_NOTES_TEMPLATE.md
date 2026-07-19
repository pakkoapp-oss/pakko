## pakko.exe — standalone CLI

Download the zip for your architecture below (`pakko-win-x64.zip` or `pakko-win-arm64.zip`), then
verify it against `SHA256SUMS` before running it:

```powershell
Get-FileHash .\pakko-win-x64.zip -Algorithm SHA256
```

Compare the printed hash against the matching line in `SHA256SUMS`.

`pakko.exe` runs standalone — no installation, no GUI/MSIX required. See `CLI.md` for the full
command reference.

---

The MSIX (GUI app) is **not** attached to this release. It's still signed with a self-signed
development certificate — sideload-only, blocked for public installs by SmartScreen/AppLocker
until real code signing (see `TASKS.md`'s T-F10) lands — and is handed to testers directly in
the meantime.
