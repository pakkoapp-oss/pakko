# DECISIONS.md — Architectural Decisions

Key decisions and rejected alternatives. Read before implementing anything in
packaging, COM, or shell integration.

---

## MSIX Satellite EXE Packaging

**Decision:** `Content Include` items in `Archiver.App.csproj` conditioned on
`GenerateAppxPackageOnBuild=true`.

**Rejected:**
- `BeforeTargets` MSBuild hooks — fires after MakeAppx regardless of target name; correct hook point changes across SDK versions and `dotnet publish` vs VS build contexts
- Manual `MakeAppx` calls in `Deploy.ps1` (unpack → inject → repack) — fragile, not incremental, duplicates SDK work
- `.wapproj` (Windows Application Packaging Project) — causes duplicate PRI resource entries (`Files/App.xbf`) when packaging multiple WinUI 3 apps; conflict cannot be resolved within the `.wapproj` model

**Reason:** `Content Include` is the only declarative approach that survives incremental builds and works identically in VS and `dotnet publish`.

---

## MSIX Signing

**Decision:** `dotnet publish` with `AppxPackageSigningEnabled=true` and
`PackageCertificateThumbprint` passed on the command line.

**Rejected:**
- Manual `SignTool.exe` calls after `MakeAppx` — produces `ERROR_BAD_FORMAT` on MSIX files

**Reason:** `New-SelfSignedCertificate` generates CNG keys by default on modern Windows. `SignTool` cannot use CNG keys to sign MSIX directly. `dotnet publish` uses a different internal code path that handles this correctly. Workaround if manual signing is ever needed: pass `-Provider "Microsoft Strong Cryptographic Provider"` to `New-SelfSignedCertificate` to force CryptoAPI (RSA 2048) instead of CNG.

---

## IExplorerCommand (Shell Context Menu)

**Decision:** Deferred to T-F61. Requires a dedicated research session before any code is written.

**Current state:** `com:Extension` and `desktop4:FileExplorerContextMenus` blocks were written in `Package.appxmanifest` (T-F55) and then temporarily removed because Explorer hangs on right-click when the registered COM EXE server does not return a valid `IExplorerCommand` implementation. Manifest registration alone is not sufficient.

**Questions to resolve before T-F61:**
- C# COM interop mechanism: `ComWrappers` vs `[ComImport]` vs WinRT interop
- How Windows Shell activates an out-of-process COM EXE server from a packaged app
- How `IShellItemArray` is passed to `IExplorerCommand::Invoke` and marshalled to file paths
- Whether `GetSubCommands` returning child objects is required for submenu population

**References to check:** NanaZip source, Windows Community Toolkit, Microsoft packaged COM server docs.

---

## Named Pipe Protocol (Shell ↔ ProgressWindow)

**Decision:** Newline-delimited UTF-8 JSON over `NamedPipeServerStream`.
- `Archiver.Shell` = server (creates pipe, runs operation)
- `Archiver.ProgressWindow` = client (connects, renders progress)
- Pipe name passed to `Archiver.ProgressWindow` via `--pipe <name>` command-line argument

**Message types:** `progress` (percent, speed, ETA), `complete`, `error`, `cancel` (client → server).
