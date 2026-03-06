# Archiver.Core.Tests.GenerateFixtures

Console tool that generates binary test fixtures for `Archiver.Core.Tests`.

## Run

```bash
dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
```

Writes fixtures to `tests/Archiver.Core.Tests/Fixtures/`. Commit the result.

## When to re-run

- A new fixture scenario is added to `Program.cs`
- An existing fixture is found to be incorrect
- `MANIFEST.sha256` is out of sync with actual files

## Manual fixtures

Some fixtures require external tools. Each has a `*_MANUAL.txt` file with instructions.

| File | Tool | Command |
|------|------|---------|
| `encrypted_aes256.zip` | 7-Zip | `7z a -p"testpassword" -mem=AES256 encrypted_aes256.zip compressible.txt` |
| `created_by_7zip.zip` | 7-Zip | `7z a created_by_7zip.zip compressible.txt` |
| `created_by_winrar.zip` | WinRAR | Add → compressible.txt → ZIP format |
| `created_by_macos.zip` | macOS | `zip -r created_by_macos.zip folder/` |
| `pakko_integrity_valid.zip` | Pakko | Run after T-34 — archive compressible.txt with Pakko |
| `pakko_integrity_tampered.zip` | Hex editor | Copy valid, flip one byte in PAKKO-INTEGRITY-V1 manifest |

After creating a manual fixture: delete the `*_MANUAL.txt` or `*_AFTER_T34.txt` file, then commit.

## How tests reference fixtures

```csharp
private static string Archive(string name) =>
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "archives", name);

private static string FixtureFile(string name) =>
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "files", name);
```

Ensure fixtures are copied to output in `Archiver.Core.Tests.csproj`:

```xml
<ItemGroup>
  <None Update="Fixtures\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```
