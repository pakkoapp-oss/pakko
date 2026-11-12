---
paths:
  - "src/Archiver.Core/**"
  - "tests/Archiver.Core.Tests/**"
  - "tests/Archiver.Core.IntegrationTests/**"
  - "tests/Archiver.Core.PerformanceTests/**"
---

# Archiver.Core rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **Any `Process.Start` of a system-provided executable must use an absolute path, not a bare
  relative name** — same reasoning as the tar.exe rule above (PATH-hijack resistance), generalized
  after SonarCloud (S4036) caught 5 `Process.Start("explorer.exe", ...)` call sites doing exactly
  the relative-name thing the tar.exe rule was meant to prevent. See
  `Archiver.Core/Services/ExplorerLauncher.cs` for the shared helper (T-F136).
- **`SafeHandle.DangerousGetHandle()` must be paired with `DangerousAddRef`/`DangerousRelease`
  spanning the actual dereference** — without it, nothing keeps the handle reachable for the GC
  between the two calls (a handle-recycling race), even though `using`/async-state-machine capture
  often makes it work by accident. Same "provable from the line itself, not hand-traced" standard
  as bounds checks. Found 16 real instances via SonarCloud S3869 — see
  `Archiver.Core/Services/Sandbox/` for the pattern (T-F136).
- **A `private` nested class cannot be a parameter type on an `internal` (or more accessible)
  method — `CS0051` "Inconsistent accessibility."** Bump the nested class to `internal` instead
  (still invisible outside the assembly without `InternalsVisibleTo`). Hit adding
  `ParallelSingleArchiveWriter.ProgressTracker` as a parameter on the existing `internal static
  CompressToTempFileAsync` (T-F140).
- **`Microsoft.Win32.Registry` (`RegistryKey`) is usable from `Archiver.Core` (plain `net11.0`,
  not `net11.0-windows`) with zero new NuGet package reference** — confirmed via a throwaway probe
  build; it's already part of the Windows runtime pack pulled in transitively, not something this
  project's "zero dependencies" constraint blocks. Mark the call site
  `[SupportedOSPlatform("windows")]` to make the resulting `CA1416` warning meaningful instead of
  leaving it unaddressed (T-F51, `GroupPolicyService`/`Win32RegistryReader`).
  **Don't over-annotate:** only the member that directly touches the Windows-only BCL API needs
  `[SupportedOSPlatform("windows")]` — a raw P/Invoke wrapper class calling its own `[LibraryImport]`s
  (e.g. `Services/Sandbox/`, `Services/Antivirus/AmsiScanner.cs`) needs no annotation at all, since
  `DllImport` itself isn't BCL-platform-tagged. Annotating the whole class anyway makes `CA1416`
  propagate into every caller, including test projects on a plain `net11.0` TFM — confirmed
  T-F146, where a class-level annotation forced two unrelated test classes to also carry the
  attribute before the warnings cleared.
- **One owner per job (T-F511).** Before writing a path or name rule, a folder walk, a temp name,
  an outcome rule or an error rule, find the job in `docs/ARCHITECTURE.md`'s "Single Owners" table.
  Extend that owner or call it; never repeat its logic at a call site. Ask the BCL first (its
  "Builds on" column). A new owner gets a row there and a guard test in the same change.
- **A public Core operation checks its paths at entry** (`InputPaths.Problem`,
  `DestinationFolder.TryCreate`) and never catches `ArgumentException` as a family per item; a new
  public method gets rows in `CoreNeverThrowsContractTests` or its completeness test fails
  (`docs/CONVENTIONS.md`, "Errors at the boundary", T-F406).
- If a change modifies a public interface, model, or contract in `Archiver.Core`, check whether
  tests in other projects (`Archiver.Shell.Tests`, future `Archiver.CLI.Tests`) need to be updated
  or extended. Internal implementation changes (private methods, buffers, sorting) require only
  `Archiver.Core.Tests` coverage.
- Before threading a new `Archiver.Core` constructor parameter (e.g. a new cross-cutting service)
  through every consumer, grep the whole repo for every `new ZipArchiveService(`/
  `new TarSandboxedService(`/etc. call site rather than trusting an older written plan's
  enumerated list — a plan can predate a newer frontend shipping. Real gap: T-F51's plan (written
  2026-07-17) enumerated only `Archiver.Shell`'s call sites; `Archiver.CLI` shipped the next day
  and was missing from it entirely.
- To unit-test an `internal` `Archiver.Core` class directly, add
  `<InternalsVisibleTo Include="Archiver.Core.Tests" />` to `Archiver.Core.csproj` rather than
  making it/its members `public` just for test access (first used for `ArchiveEntrySecurity`, T-F94).
- `ConflictBehavior.Rename` on `ZipArchiveService.ExtractAsync` means **per-file rename inside a
  merged existing folder** (the GUI app's tested behavior) — it does NOT mean "always create a
  fresh whole folder." For shell-only "always fresh" behavior (numbered folder), use
  `ExtractOptions.SeparateFolderName` computed by the caller instead of changing this semantic.
- **A zero-byte entry is always written as `Store`** (method 0), never Deflate, in both writers
  (`ZipEntryCompressor` checks the input length, not the requested level), as `ZipArchiveEntry`
  does. Up to .NET 10, `DeflateStream` wrote 0 bytes for empty input, which 7-Zip rejects as
  corrupt while .NET's own reader accepts it (T-F35 follow-up, found by a NanaZip cross-check);
  .NET 11 writes `03 00`, a valid empty block (measured 2026-11-11, T-F492), but the rule stays:
  `ZipWriterCharacterizationTests.ArchiveAsync_EmptyFile_IsStoredNotDeflated` checks both writers.
- **The parallel writer's in-memory entries deflate through the run's `DeflateEncoderPool`**
  (T-F318): a `DeflateStream` per entry there brings back dotnet/runtime#134700's page-fault storm
  (about 18 faults per entry, `Archive/ManySmallFiles` 1.2 against 0.96). `DeflateEncoder.Reset()`
  is not a fresh encoder (T-F499): the pool's `Deflater` scrubs the window between entries, so
  never call an encoder's `Reset()` and reuse it directly - the bytes then depend on what it
  compressed before, and T-F31's byte-identical archives break.
- **Diagnosing ZIP format bugs:** `7za.exe l -slt <archive>` (the vendored copy under
  `tests/Archiver.Core.PerformanceTests/Tools/7-Zip/x64/`) dumps per-entry technical fields
  (Method, Size, Packed Size, CRC, Attributes) — the fastest way to see exactly what a hand-rolled
  writer actually produced, and to reproduce a real-world `7za`/NanaZip extraction failure
  without needing NanaZip itself installed.
- **Benchmarking new CPU-bound parallel code in a fresh `dotnet test` process can show wildly
  bimodal timing** (e.g. 0.36s vs 1.2s+ for the identical 300 MB CRC-32 chunk-hash) — root cause
  was .NET's default `ThreadPool` thread-injection ramp-up (~1 new thread per ~500 ms under
  demand), not the algorithm. Fix: a one-time `ThreadPool.SetMinThreads(Environment.ProcessorCount,
  ...)` before the parallel section, and prefer synchronous `Parallel.For`/`RandomAccess.Read`
  over `Parallel.ForAsync`/`RandomAccess.ReadAsync` for CPU+I/O-bound chunked work — avoids
  async-state-machine/completion-port scheduling entirely (same reasoning as the `useAsync: false`
  `FileStream` convention already noted above). See `FileHashService.
  ComputeFileCrc32ParallelAsync`/`Crc32.Combine` (T-F128) for the working pattern.
