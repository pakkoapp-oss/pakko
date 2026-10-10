---
paths:
  - "tests/**"
---

# Test rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- When adding or modifying tests, always run `dotnet test --filter "Category!=Slow&Category!=VeryLarge"`
  with no path argument — never scope to a single test project. **Plain `Category!=Slow` alone is
  not sufficient** — a test tagged only `VeryLarge` (not `Slow`) is not excluded by `!=Slow`, so it
  would run automatically, defeating the entire point of the `VeryLarge` tier (confirmed empirically
  2026-07-17: `Category!=Slow` alone picked up T-F114's two one-large-file tests). All projects must
  stay green after every change. This combined filter excludes T-F20's Zip64 Slow tests and T-F114's
  Slow-tagged performance tests (real multi-second cost); run `dotnet test --filter "Category=Slow"`
  too before a release or when the change touches Zip64-adjacent code (entry counts, large files,
  Zip64 boundary conditions) or compression/extraction performance. `dotnet test --filter
  "Category=VeryLarge"` (the >4 GiB Zip64 test, T-F114's one-large-file scenarios) is on-demand
  only — never run automatically as part of either of the above, only when deliberately verifying
  that specific path.
- **When writing a regression test for a just-fixed bug, temporarily revert the fix and confirm
  the new test actually fails before restoring it and leaving the test green** — a test that only
  ever ran against already-fixed code can pass for the wrong reason (e.g. testing a whitebox seam
  that bypasses the real bug). This discipline itself caught a second, independent bug this way
  (T-F140): a progress-throttle timestamp initialized at construction silently swallowed the very
  first report for any fast/small-file operation — invisible until a fast-operation test was
  deliberately run against the pre-fix code and didn't fail as expected.
- **A `file`-scoped type (e.g. a hand-rolled `file sealed class FakeX` test fake) cannot appear in
  the signature of a non-`file`-scoped member — `CS9051`.** If a test helper method needs the fake
  as an explicit parameter type, drop the `file` modifier on the fake class instead (plain
  top-level `internal` is fine — it's still test-assembly-only). Only matters when a shared helper
  takes the fake by type; a fake only ever assigned to `var` never hits this (T-F146).

## Known test gaps — manual verification required

- **Observed test flakiness (2026-07-07):** `Extract_ValidUnicodeFilenames_Succeeds` and
  `ExtractAsync_ZipWithMotw_PropagatesZoneIdentifierToExtractedFiles` each failed once in a
  run, then passed immediately on rerun in isolation — looks like parallel-execution timing
  noise, not a real regression. If a test fails once, rerun before treating it as caused by
  your change.
  **Root-caused and fixed 2026-07-24 (T-F130):** all 10 `Archiver.Core.IntegrationTests` classes
  that drive real AppContainer/Job Object/quarantine ACL calls were racing against *each other*
  under xUnit's default parallel-by-class execution — grouped into one
  `[Collection("TarSandbox", DisableParallelization = true)]` (see `docs/TESTING.md`) so they run
  sequentially relative to each other while still running in parallel with unrelated projects.
  **Confirmed in a real CI run on the actual fix** (run `30037580723`, 2026-07-23: 60/60,
  0 failures, 8s — not just the local `dotnet test` pass or the earlier pre-fix "clean rerun,"
  which only demonstrated the intermittent-failure pattern, not this fix's effect). If this specific
  flakiness class recurs
  recurs anyway, the likely remaining vector is cross-*project* contention (`Archiver.CLI.Tests`'
  `Subprocess/` layer launching real sandboxed subprocesses concurrently with this project, not
  just within it) — that would need a similar fix scoped across both projects, not assumed already
  covered by the single-project Collection above.
  **T-F373 (2026-10-10):** a test that holds an inheritable handle and waits for its EOF fails when
  another class in the same assembly `Process.Start`s with redirected stdio meanwhile (the child
  inherits every inheritable handle); such a test goes in its own `DisableParallelization` collection.
  **T-F162 (2026-08-11):** a test waiting on `System.Progress<T>`'s callback can time out on a
  loaded runner (it posts to the ThreadPool); use the synchronous hand-rolled `IProgress<T>` fake
  instead (see `docs/DECISIONS.md`'s T-F162 entry).
- **T-F143 SonarCloud coverage triage (2026-08-06) — categories left deliberately uncovered by
  design, not by oversight:** `ExplorerLauncher`'s OS-side-effect callers (4 call sites — opening
  a real Explorer window isn't something a unit test should trigger); native Win32/subprocess
  fault-injection paths (`SandboxedProcessLauncher`, `TarSandboxedService.RunUnsandboxedTarAsync`'s
  `process.Kill()` cleanup — forcing these requires simulating OS-level failures, not worth the
  brittleness); best-effort estimation-helper `catch` blocks; duplicate `UnauthorizedAccessException`/
  generic-`Exception` catch variants where only the `IOException` sibling is tested (same code
  shape, marginal value); and `ZipArchiveService.ArchiveSingleSeparatePathAsync`'s "zero entries
  written" branch (line ~448) plus `ParallelSingleArchiveWriter`'s CAS-retry-loop race — both left
  open questions, the exact real-world trigger for the former wasn't confirmed within that task's
  budget (T-F66 already makes plain empty folders write a placeholder entry, so what else still
  reaches it is unclear). See `docs/TASKS_DONE.md`'s T-F143 entry for the full triage and the 40 tests
  that *were* added to close the actual gate-blocking gaps.
- **The vendored `7za.exe` test dependency (T-F114, `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`)
  never enters this pipeline.** `Deploy.ps1` only publishes `src/Archiver.App`; nothing under
  `tests/` is packaged, signed, or installed. See `SECURITY.md`'s "Vendored 7-Zip" section if this
  ever needs re-confirming.
