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

**Prefer the Microsoft Store** if you just want to install Pakko without warnings:
https://apps.microsoft.com/detail/9p5mw010d8pr — Microsoft's own signature, no SmartScreen/
AppLocker friction, auto-updates.

The `.msix`/`.msixbundle` below is instead signed with a **self-signed development certificate**,
not a publicly trusted one — installing it will trigger SmartScreen/AppLocker warnings, and it
will not sideload at all unless the signing certificate is explicitly trusted on the target
machine first (see `scripts/README.md`). It exists for testing a specific pre-release build, or
for environments that can't use the Store. Public code-signing for this direct-download path is
still being worked (see `docs/TASKS.md`'s T-F10).
