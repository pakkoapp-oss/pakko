---
paths:
  - "src/Archiver.Shell/**"
  - "src/Archiver.CLI/**"
  - "tests/Archiver.Shell.Tests/**"
  - "tests/Archiver.CLI.Tests/**"
---

# Archiver.Shell / Archiver.CLI rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **.NET COM interop (`[ComImport]` interfaces consuming external COM objects):** check the real
  SDK header before declaring the interface — if a method returns a plain type (e.g. `BOOL`)
  instead of `HRESULT`, mark it `[PreserveSig]`. Without it, the marshaller assumes the
  HRESULT + hidden-`[out]`-param convention and silently misreads the return value. Real bug:
  `IProgressDialog.HasUserCancelled` always read back `false` (Cancel appeared to do nothing)
  until `[PreserveSig]` was added — see `Archiver.Shell/NativeProgressDialog.cs`.
- **Console-frontend testing (`Archiver.Shell`, future `Archiver.CLI`):** extract argument
  parsing into its own testable class (e.g. `ShellArgumentParser`) and unit-test it in-process —
  never parse inline in `Main`. No test in this repo spawns a built `.exe` and asserts on a real
  exit code/stdout yet — `Archiver.Shell.Tests` only unit-tests the parser, which is fine there
  since its args are always generated programmatically, never typed by a person. A frontend a
  user/script invokes directly (`Archiver.CLI`) needs that real-process layer too, since its
  exit code/stdout *is* the public contract — see T-F09's acceptance criteria for the shape.
- **`Archiver.Shell.exe`'s CLI commands (`--archive`, `--extract-here`, `--extract-folder`,
  `--test`, `--hash`) take only source/archive paths — never an explicit destination.** The
  destination is always auto-computed (folder-of-source for extract, `<name>.zip` next to the
  source for archive — same naming Explorer's "Add to X.zip" verb produces). Passing an extra path
  as a destination gets silently treated as another source/archive path instead (T-F142).
- **Any Pakko command that shows a native modal (`IProgressDialog`, `MessageBoxW` result dialogs —
  `--archive`, `--extract-*`, `--test`, `--scan`, `--hash`) blocks forever if invoked directly
  (`& $exe args`) from the PowerShell tool** — the call never returns because the dialog waits for
  a click. Launch via `Start-Process -FilePath $exe -ArgumentList @(...)` (detached) instead, then
  poll for the dialog with `EnumWindows`/`GetWindowThreadProcessId` filtered to the child PID, read
  its result text via `EnumChildWindows`, and dismiss with `SendMessage(hWnd, 0x00F5, ...)`
  (BM_CLICK) on the OK button's handle. If a call hangs anyway, `taskkill /F /IM
  Archiver.Shell.exe` clears the stuck modal before retrying (T-F151/T-F153 smoke tests).
- **NativeProgressDialog (Archiver.Shell)** — the `IProgressDialog` COM wrapper is not covered
  by automated tests (COM UI object, not unit-testable). Manual verification required: progress
  bar and status line update during Extract/Archive, Cancel button stops the operation.
