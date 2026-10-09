---
paths:
  - "src/Archiver.Shell/**"
  - "src/Archiver.CLI/**"
  - "tests/Archiver.Shell.Tests/**"
  - "tests/Archiver.CLI.Tests/**"
---

# Archiver.Shell / Archiver.CLI rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **.NET COM interop (`[GeneratedComInterface]` interfaces consuming external COM objects; `[ComImport]` is banned under AOT):** check the real
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
- **NativeProgressDialog (Archiver.Shell)** — the `IProgressDialog` COM wrapper is not covered
  by automated tests (COM UI object, not unit-testable). Manual verification required: progress
  bar and status line update during Extract/Archive, Cancel button stops the operation.
