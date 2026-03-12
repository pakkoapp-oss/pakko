# AGENT.md — Pakko

**Entry point for AI coding agents (Claude Code / OpenAI Codex).**
Read this file first. It references other files in order.

---

## Project in One Sentence

**Pakko** — minimal WinUI 3 desktop app wrapping Windows built-in ZIP support (`System.IO.Compression`) with a clean GUI, planned shell extension (IExplorerCommand), and tar.exe integration for RAR/7z/tar extraction. No third-party compression libraries. Target: Ukrainian government/defense — trust, auditability, minimal attack surface.

---

## Status

**v1.0 complete** — all T-01 through T-35 + T-11 done, tagged `v1.0.0`.
Next work is Future tasks (T-F01+) in `TASKS.md`.

---

## Read Order

1. `AGENT.md` ← you are here
2. `CLAUDE.md` — session context, current state, build commands
3. `TASKS.md` — active/future tasks with acceptance criteria
4. `ARCHITECTURE.md` — current C# signatures (use these, do not invent)
5. `CONVENTIONS.md` — coding rules
6. `SECURITY.md` — threat model (if modifying compression logic)

For completed task reference: `TASKS_DONE.md`

Optional reference (research context, do not implement without task):
- `RESEARCH.md` — architecture research findings (if present)
- `windows-archiver-reality-2026.md` — Windows 11 archive ecosystem analysis (if present)

---

## Hard Constraints (never violate)

| Rule | Reason |
|------|--------|
| No NuGet compression packages | Auditability, no external deps |
| Use only `System.IO.Compression` for ZIP | Native Windows, verifiable |
| MVVM strictly | Testability, separation of concerns |
| `Archiver.Core` zero WinUI references | Layer independence |
| `Archiver.Core` zero `ResourceLoader` references | Layer independence |
| `Archiver.Core` zero `ILogService` references | Layer independence |
| No background services | Out of scope |
| tar.exe absolute path only (`C:\Windows\System32\tar.exe`) | Prevent EXE hijacking via PATH |
| No in-process libarchive | Memory safety, no C/C++ parser in-process |
| MOTW propagation always on (v1.2+) | Security requirement — prevents macro attacks |

---

## Repo Layout

```
windows-archiver-wrapper/
├── src/
│   ├── Archiver.Core/          ← net8.0 class library, no UI deps
│   └── Archiver.App/           ← WinUI 3 app
├── tests/
│   ├── Archiver.Core.Tests/    ← xunit, 45 tests
│   └── Archiver.Core.Tests.GenerateFixtures/
├── AGENT.md, CLAUDE.md, TASKS.md, TASKS_DONE.md
├── ARCHITECTURE.md, CONVENTIONS.md, SECURITY.md
└── windows-archiver-wrapper.sln
```

---

## Agent Workflow

When implementing a task from `TASKS.md`:

1. Read task — check acceptance criteria
2. Check `ARCHITECTURE.md` — use existing signatures, do not invent new ones
3. Implement
4. `dotnet build src/Archiver.Core` — verify no errors
5. `dotnet test tests/Archiver.Core.Tests` — verify 45+ tests pass
6. Mark task `[x]` in `TASKS.md`
