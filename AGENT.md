# AGENT.md — Windows Archiver Wrapper

**Entry point for AI coding agents (Claude Code / OpenAI Codex).**  
Read this file first. It references other files in order.

---

## Read Order

1. `AGENT.md` ← you are here
2. `SPEC.md` — what to build and why
3. `ARCHITECTURE.md` — project structure and layer contracts
4. `BOOTSTRAP.md` — DI wiring and app startup
5. `XAML.md` — UI skeleton (use as-is, do not redesign)
6. `TASKS.md` — implementation tasks with acceptance criteria
7. `CONVENTIONS.md` — coding rules the agent must follow
8. `TESTING.md` — test project setup and all test cases

---

## Project in One Sentence

A minimal WinUI 3 desktop app that wraps Windows built-in ZIP support (`System.IO.Compression`) with a clean GUI — no third-party compression libraries.

---

## Hard Constraints (never violate)

| Rule | Reason |
|------|--------|
| No NuGet compression packages | Stability, no external deps |
| Use only `System.IO.Compression` | Native Windows API |
| MVVM pattern strictly | Testability, separation of concerns |
| `Archiver.Core` has zero WinUI references | Layer independence |
| No background services | Out of scope for v1.0 |

---

## Repo Layout

```
windows-archiver-wrapper/
├── src/
│   ├── Archiver.Core/          ← class library, no UI deps
│   └── Archiver.App/           ← WinUI 3 app
├── docs/
├── AGENT.md
├── SPEC.md
├── ARCHITECTURE.md
├── TASKS.md
├── CONVENTIONS.md
└── windows-archiver-wrapper.sln
```

---

## Quick Bootstrap Commands

```bash
# 1. Create solution
dotnet new sln -n windows-archiver-wrapper

# 2. Create core library
dotnet new classlib -n Archiver.Core -o src/Archiver.Core --framework net8.0

# 3. Add core to solution
dotnet sln add src/Archiver.Core

# 4. Create WinUI app (Visual Studio only — CLI does not support WinUI template)
#    Template: "Blank App, Packaged (WinUI 3 in Desktop)"
#    Project name: Archiver.App
#    Output: src/Archiver.App

# 5. Add app to solution
dotnet sln add src/Archiver.App

# 6. Add Core reference to App
dotnet add src/Archiver.App reference src/Archiver.Core
```

---

## Agent Workflow

When implementing a task from `TASKS.md`:

1. Check task status — skip if marked `[x]`
2. Implement according to the interface/signature defined in `ARCHITECTURE.md`
3. Verify acceptance criteria listed in the task
4. Do not modify files outside the task scope
5. Mark task `[x]` in `TASKS.md` after completion
