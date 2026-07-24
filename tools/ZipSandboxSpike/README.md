# ZipSandboxSpike

T-F132 empirical spike, not a shipped product component — never referenced by
`src/Archiver.App`/`Archiver.Shell`/`Archiver.CLI`, never packaged into the
MSIX. Same status as the vendored, test-only `7za.exe` reference in
`tests/Archiver.Core.PerformanceTests/Tools/7-Zip/`.

A minimal worker that runs `ZipArchiveService.ArchiveAsync`/`ExtractAsync`
directly (bypassing `ExtractionRouter`/`ArchiveCreationRouter`, and therefore
`TarSandboxedService`'s unsandboxed `tar.exe --version` capability check —
see `Program.cs`'s own comment for why). Used by
`tests/Archiver.Core.PerformanceTests/ZipSandboxSpikePerformanceTests.cs` to
measure the real overhead of launching this worker inside an AppContainer
sandbox versus calling `ZipArchiveService` in-process, per `docs/TASKS.md`'s
T-F132 entry.

## Publishing

Not built automatically by `dotnet test` — publish once manually before
running the spike tests:

Run from the repo root — `-p:PublishDir` is resolved relative to the *project* file, not the
working directory, so a bare relative path lands under `tools\ZipSandboxSpike\artifacts\...`
instead of the repo-root `artifacts\` every other publish output uses. Pass an absolute path (or
`..\..\artifacts\...`) to land in the right place:

```powershell
dotnet publish tools\ZipSandboxSpike\ZipSandboxSpike.csproj -c Release -r win-x64 --self-contained true -p:PublishDir="$PWD\artifacts\zip-sandbox-spike\win-x64"
```

`artifacts/` is already gitignored. Re-publish after any change to
`Program.cs` or to `Archiver.Core`'s ZIP path before trusting new numbers.

## CLI protocol

```
ZipSandboxSpike.exe archive <sourceDir> <destZipPath>
ZipSandboxSpike.exe extract <archiveZipPath> <destDir>
```

Exit code `0` on success, `1` on failure (error lines on stderr), `2` on bad
usage. Always prints `internal_elapsed_ms=<n>` to stderr — the worker's own
`Stopwatch`-measured time for the `ZipArchiveService` call, separate from the
launcher-side total (spawn + CLR/JIT cold start + AppContainer/pipe/wait
teardown) the test measures around the whole process launch.
