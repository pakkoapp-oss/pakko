---
paths:
  - "src/Archiver.App/**"
  - "src/Archiver.App.Core/**"
  - "src/Archiver.OperationUi/**"
  - "src/Archiver.OperationUi.Core/**"
---

# Archiver.App / WinUI 3 rules

Moved out of the root `CLAUDE.md` (T-F369); loads when a file matching the paths above is read or edited.

- **Native AOT (T-F355): all four exes (App, Shell, OperationUi, `pakko`) ship Native AOT; the five
  libraries are `IsAotCompatible`.** The build fails on IL2xxx/IL3xxx/CsWinRT warnings, but `dotnet
  test` runs under JIT and cannot see an AOT-only failure. No `Assembly.Load*`, `Reflection.Emit`,
  reflection over unknown types, `[ComImport]` (use `[GeneratedComInterface]`) or reflection JSON
  (use a `JsonSerializerContext`). WinUI: a value read from a resource dictionary (`Resources[...]`,
  `ThemeDictionaries`, `TryGetValue`) is cast with `WinRT.CastExtensions.As<T>(...)`, never `(T)` or
  `as T` (a test reads the source); a class implementing a WinRT or mapped .NET interface is
  `partial`; only `x:Bind` (`{Binding}` needs `[GeneratedBindableCustomProperty]`); no collection
  expression handed to WinRT; a new collection bound to `ItemsSource` gets a device check. Never
  `UseSystemResourceKeys`/`InvariantGlobalization`. ILCompiler needs `vswhere.exe` on PATH. Verify
  App/Shell/OperationUi changes on the deployed package, not under the debugger. Examples:
  `docs/CONVENTIONS.md`; why: `docs/DECISIONS.md` T-F355.
- **UI-thread marshaling for Core→App callbacks:** any delegate `Archiver.Core` invokes that ends
  up showing WinUI (e.g. `ExtractOptions.ConfirmCompressionBombExtraction` → `ContentDialog`) must
  marshal onto `Window.DispatcherQueue` inside the App-layer implementation —
  `ZipArchiveService`/`TarProcessService` run their extraction bodies off the UI thread, and
  `ContentDialog.ShowAsync()` requires the calling thread to own the DispatcherQueue. Found via
  design review before shipping (T-F94) — would have crashed on first real use otherwise.
- **WinUI 3 cold-start activation gotcha:** `AppInstance.Activated` (Windows App SDK) only fires
  for activations *redirected* to an already-running instance — never for a process's own initial
  activation. `OnLaunched` must pull it explicitly via
  `AppInstance.GetCurrent().GetActivatedEventArgs()` and route File/Launch kinds through the same
  handler `OnActivated` uses, or a cold Explorer/file-association launch silently opens a blank
  window (see T-F83 in `DECISIONS.md`).
- **Shared WinUI `x:Uid` across elements with different property sets is fatal, not a no-op:**
  giving a `Button` (`.Content`) and a `TextBlock` (`.Text`) the same `x:Uid` applies both resource
  keys to both elements regardless of which properties exist — crashes natively (`0xc000027b`) at
  `InitializeComponent()`. Give every distinct element/property combo its own key (T-F05,
  `DECISIONS.md`).
- **`ListView` already virtualizes by default** (its own `ItemsStackPanel`) — don't add an explicit
  `VirtualizingStackPanel` `ItemsPanel` without a specific reason. Doing so gratuitously can race
  with an async-loaded bound property (a fire-and-forget `Task.Run` setting a value after
  construction), leaving a freshly realized row blank until a forced re-layout (T-F05,
  `DECISIONS.md`).
- **A child element's `MinHeight` (e.g. a `ListView`'s own `MinHeight="80"`) does NOT force a
  Grid's Star-sized (`*`) row to grow past what the row-sizing algorithm allocates** — the
  `RowDefinition` itself needs the `MinHeight`. Enough sibling `Auto` rows can otherwise clamp
  the Star row to 0, and every child inside measures/arranges within zero height regardless of
  data, binding mode, or population timing — cost five separate disproven fix hypotheses before
  being found (T-F106, `DECISIONS.md`).
- **A dotted resw key (`"Foo.Content"`) manually looked up via `_res.GetString("Foo.Content")`
  silently returns an empty string if no element in XAML actually has `x:Uid="Foo"`.** The dotted
  naming convention only gets populated by the XAML framework's implicit `x:Uid` + property-suffix
  lookup — a key that exists in every locale's `.resw` but was never wired to an `x:Uid` is dead,
  and manual `GetString()` won't resolve it either. For any string accessed manually from C# (not
  via `x:Uid`), use a plain, non-dotted key name, matching `StatusReady`/`StatusArchiving`/etc.
  Real bug: `MainViewModel.ArchiveButtonText`/`ExtractButtonText` looked up `"ArchiveButton.Content"`
  and got blank buttons in every locale until renamed to plain `ArchiveButtonLabel` (T-F104).
- **Localization (`Strings/<locale>/Resources.resw`, T-F91):** `Package.appxmanifest`'s
  `<Resource Language="x-generate"/>` auto-detects every `Strings/<locale>/` folder at build —
  no manual `<Resources>` edit needed when adding a locale. A key missing from a locale's
  `Resources.resw` falls back to `en-US` automatically, so non-translatable keys (URLs) should
  be omitted from locale files, not duplicated. Verify a new locale is wired without opening VS:
  `dotnet build src/Archiver.App/Archiver.App.csproj /p:Platform=x64`, then check
  `bin/x64/Release/net10.0-windows10.0.17763.0/win-x64/AppxManifest.xml` for the `<Resource
  Language>` entries.
