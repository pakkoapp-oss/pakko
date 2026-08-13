# API Reference

Generated from the XML doc comments in `Archiver.Core` and `Archiver.App.Core` — Pakko's two
WinUI-free class libraries (see [Architecture](../docs/ARCHITECTURE.md) for how they fit into the
rest of the app). Browse by namespace in the sidebar, or start here:

- **[Archiver.Core.Services](xref:Archiver.Core.Services)** — archive/extract services, sandboxed
  tar.exe integration, hashing, AMSI scanning, Group Policy
- **[Archiver.Core.Interfaces](xref:Archiver.Core.Interfaces)** — the public service contracts
  (`IArchiveService`, `IExtractionRouter`, `ITarService`, etc.)
- **[Archiver.Core.Models](xref:Archiver.Core.Models)** — options/result records shared across
  every service (`ArchiveOptions`, `ExtractOptions`, `ArchiveResult`, `ConflictBehavior`, etc.)
- **[Archiver.App.Core](xref:Archiver.App.Core)** — Archive Browser support (tree indexing,
  nested-archive drill-down, file-activation routing) shared by the WinUI app

Full coverage (T-F173, 2026-08-13): every public type/member has a real summary, or is a
self-documenting property/enum whose CS1591 gate is suppressed per-file via `.editorconfig`
(Models/ViewModels — see `docs/CONVENTIONS.md`'s XML Documentation section for the exact rule).
Any gap you find here is a real omission worth filing, not an expected placeholder.
