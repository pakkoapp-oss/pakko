# CONVENTIONS.md — Coding Conventions

AI agents must follow these rules in all generated code.

---

## C# Language Rules

| Rule | Value |
|------|-------|
| Language version | C# 12 |
| Nullable reference types | `enable` (all projects) |
| Implicit usings | `enable` |
| File-scoped namespaces | Required |
| `var` | Use only when type is obvious from right-hand side |
| String interpolation | Prefer over `string.Format` |
| `record` vs `class` | Use `record` for data-only types (models, options) |
| `sealed` | Apply to all classes not designed for inheritance |

---

## Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Namespace | `PascalCase` | `Archiver.Core.Models` |
| Class / Record | `PascalCase` | `ZipArchiveService` |
| Interface | `IPascalCase` | `IArchiveService` |
| Public property | `PascalCase` | `SourcePaths` |
| Private field | `_camelCase` | `_archiveService` |
| Method | `PascalCase` | `ArchiveAsync` |
| Local variable | `camelCase` | `archivePath` |
| Async method | Suffix `Async` | `ExtractAsync` |
| Enum | `PascalCase` | `ArchiveMode.SingleArchive` |

---

## Async Rules

- All IO operations must be `async/await`
- Never use `.Result` or `.Wait()` on tasks
- Always accept `CancellationToken` in public async methods
- Always name the parameter `cancellationToken` (not `ct` or `token`)
- Use `ConfigureAwait(false)` in `Archiver.Core` (no UI context needed)

```csharp
// Correct
await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);

// Wrong
File.WriteAllBytes(path, data);
```

---

## Error Handling Rules

- `Archiver.Core` services must **never throw** to callers
- Catch `Exception` only at service boundaries, not inside helpers
- All exceptions → `ArchiveError` with `SourcePath`, `Message`, `Exception`
- Log-friendly: `ArchiveError.Message` must be human-readable (not exception type name)

```csharp
// Correct pattern in ZipArchiveService
try
{
    // ... operation
}
catch (IOException ex)
{
    errors.Add(new ArchiveError
    {
        SourcePath = sourcePath,
        Message = $"Cannot access file: {ex.Message}",
        Exception = ex
    });
}
```

---

## MVVM Rules

- ViewModels do **not** reference WinUI controls directly
- ViewModels do **not** use `Dispatcher` — use `ObservableCollection` thread-safe updates
- Code-behind (`.xaml.cs`) contains only:
  - Constructor with `InitializeComponent()`
  - Event handlers that immediately delegate to ViewModel
  - Drag-and-drop wiring
- Services injected via constructor (no service locator, no static access)

```csharp
// Correct ViewModel constructor
public MainViewModel(IArchiveService archiveService, IDialogService dialogService)
{
    _archiveService = archiveService;
    _dialogService = dialogService;
}

// Wrong
public MainViewModel()
{
    _archiveService = new ZipArchiveService(); // hard dependency
}
```

---

## File Organization

- One type per file
- File name matches type name exactly: `ZipArchiveService.cs` contains `ZipArchiveService`
- No partial classes unless required by WinUI XAML code-behind
- `using` directives: system namespaces first, then project namespaces, no blank lines between

---

## XML Documentation

Required on:
- All `public` interfaces
- All `public` service classes
- All `public` methods in `Archiver.Core`

Not required on:
- `private` members
- ViewModels (UI layer)
- Models (self-documenting by property names)

```csharp
/// <summary>
/// Creates ZIP archives from the provided options.
/// </summary>
/// <param name="options">Archive configuration including source paths and destination.</param>
/// <param name="progress">Optional progress reporter (0–100).</param>
/// <param name="cancellationToken">Token to cancel the operation between items.</param>
/// <returns>Result containing created file paths and any per-item errors.</returns>
Task<ArchiveResult> ArchiveAsync(
    ArchiveOptions options,
    IProgress<int>? progress = null,
    CancellationToken cancellationToken = default);
```

---

## .editorconfig

Place this file at the repository root:

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
indent_style = space
indent_size = 4
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
# Namespace style
csharp_style_namespace_declarations = file_scoped:error

# var preferences
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion

# Expression-bodied members
csharp_style_expression_bodied_methods = false:silent
csharp_style_expression_bodied_properties = true:suggestion

# Null checking
dotnet_style_null_propagation = true:suggestion
dotnet_style_coalesce_expression = true:suggestion

# Using directives
csharp_using_directive_placement = outside_namespace:error

# Modifier order
csharp_preferred_modifier_order = public,private,protected,internal,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,volatile,async:suggestion

# Naming rules
dotnet_naming_rule.private_fields_should_be_camel_case.severity = warning
dotnet_naming_rule.private_fields_should_be_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_should_be_camel_case.style = camel_case_underscore_prefix

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_underscore_prefix.capitalization = camel_case
dotnet_naming_style.camel_case_underscore_prefix.required_prefix = _

[*.xaml]
indent_size = 4

[*.md]
trim_trailing_whitespace = false

[*.json]
indent_size = 2

[*.{csproj,props,targets}]
indent_size = 2
```

---

## Packages Allowed per Project

| Package | Project | Purpose |
|---------|---------|---------|
| `CommunityToolkit.Mvvm` | `Archiver.App` only | `ObservableObject`, `RelayCommand` |
| None | `Archiver.Core` | Pure .NET, no NuGet dependencies |
