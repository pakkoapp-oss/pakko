# API Reference

Generated from the XML doc comments in `Archiver.Core` and `Archiver.App.Core` — Pakko's two
WinUI-free class libraries (see [Architecture](../docs/ARCHITECTURE.md) for how they fit into the
rest of the app). Browse by namespace in the sidebar, or start here:

- **[Archiver.Core.Services](Archiver.Core.Services.html)** — archive/extract services, sandboxed
  tar.exe integration, hashing, AMSI scanning, Group Policy
- **[Archiver.Core.Interfaces](Archiver.Core.Interfaces.html)** — the public service contracts
  (`IArchiveService`, `IExtractionRouter`, `ITarService`, etc.)
- **[Archiver.Core.Models](Archiver.Core.Models.html)** — options/result records shared across
  every service (`ArchiveOptions`, `ExtractOptions`, `ArchiveResult`, `ConflictBehavior`, etc.)
- **[Archiver.App.Core](Archiver.App.Core.html)** — Archive Browser support (tree indexing,
  nested-archive drill-down, file-activation routing) shared by the WinUI app

Coverage is partial today — some members show no summary because their `///` doc comments haven't
been written yet (tracked as T-F173, see `docs/TASKS.md`). Missing docs are not build errors yet;
that's a deliberate first phase, not an oversight.
