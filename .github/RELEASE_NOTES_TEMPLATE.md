## pakko.exe — standalone CLI

Download the zip for your architecture below (`pakko-win-x64.zip` or `pakko-win-arm64.zip`), then
verify it against `SHA256SUMS` before running it:

```powershell
Get-FileHash .\pakko-win-x64.zip -Algorithm SHA256
```

Compare the printed hash against the matching line in `SHA256SUMS`.

`pakko.exe` runs standalone — no installation, no GUI/MSIX required. See `docs/CLI.md` for the full
command reference.

---

## Pakko — MSIX (GUI app)

The `.msix`/`.msixbundle` below is signed with a **self-signed development certificate**, not a
publicly trusted one — installing it will trigger SmartScreen/AppLocker warnings, and it will not
sideload at all unless the signing certificate is explicitly trusted on the target machine first
(see `scripts/README.md`). Public installs without warnings are planned once real code signing
lands (see `docs/TASKS.md`'s T-F10/T-F124).
