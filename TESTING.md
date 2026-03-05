# TESTING.md — Test Plan

Covers `Archiver.Core` only. UI layer (`Archiver.App`) is not unit-tested in v1.0.

---

## Test Project Setup

```bash
dotnet new xunit -n Archiver.Core.Tests -o tests/Archiver.Core.Tests --framework net8.0
dotnet sln add tests/Archiver.Core.Tests
dotnet add tests/Archiver.Core.Tests reference src/Archiver.Core
```

**Packages for test project:**

```xml
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

---

## Test File Structure

```
tests/
└── Archiver.Core.Tests/
    ├── Archiver.Core.Tests.csproj
    ├── Services/
    │   ├── ZipArchiveServiceArchiveTests.cs
    │   └── ZipArchiveServiceExtractTests.cs
    ├── Models/
    │   └── ArchiveOptionsTests.cs
    └── Helpers/
        └── TempDirectory.cs
```

---

## TempDirectory Helper

All tests use this to avoid polluting the filesystem:

```csharp
// Helpers/TempDirectory.cs
namespace Archiver.Core.Tests.Helpers;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        System.IO.Path.GetRandomFileName());

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string CreateFile(string name, string content = "test content")
    {
        var path = System.IO.Path.Combine(Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
```

---

## Archive Tests — ZipArchiveServiceArchiveTests.cs

```csharp
namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceArchiveTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ArchiveAsync_SingleFile_CreatesZip()
    {
        var file = _temp.CreateFile("document.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
        result.CreatedFiles[0].Should().EndWith(".zip");
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SingleArchiveMode_CreatesOneZip()
    {
        var file1 = _temp.CreateFile("a.txt");
        var file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            ArchiveName = "combined",
            Mode = ArchiveMode.SingleArchive
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SeparateArchivesMode_CreatesMultipleZips()
    {
        var file1 = _temp.CreateFile("a.txt");
        var file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task ArchiveAsync_NonExistentFile_ReturnsErrorNotThrows()
    {
        var options = new ArchiveOptions
        {
            SourcePaths = [@"C:\does\not\exist.txt"],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].SourcePath.Should().Be(@"C:\does\not\exist.txt");
        result.Errors[0].Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_CancellationRequested_StopsProcessing()
    {
        var files = Enumerable.Range(1, 10)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options, cancellationToken: cts.Token);

        // Should process 0 or fewer than 10 items
        result.CreatedFiles.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task ArchiveAsync_ReportsProgress()
    {
        var files = Enumerable.Range(1, 5)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        await _sut.ArchiveAsync(options, progress);
        await Task.Delay(50); // let Progress callbacks fire

        progressValues.Should().NotBeEmpty();
        progressValues.Last().Should().Be(100);
    }
}
```

---

## Extract Tests — ZipArchiveServiceExtractTests.cs

```csharp
namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceExtractTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateTestZip(string zipName, params string[] fileNames)
    {
        var zipPath = Path.Combine(_temp.Path, zipName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var name in fileNames)
            archive.CreateEntryFromFile(_temp.CreateFile(name), name);
        return zipPath;
    }

    [Fact]
    public async Task ExtractAsync_ValidZip_ExtractsFiles()
    {
        var zip = CreateTestZip("archive.zip", "file1.txt", "file2.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(destDir).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SeparateFoldersMode_CreatesSubfolderPerArchive()
    {
        var zip1 = CreateTestZip("first.zip", "a.txt");
        var zip2 = CreateTestZip("second.zip", "b.txt");
        var destDir = Path.Combine(_temp.Path, "extracted");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip1, zip2],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "first")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "second")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_InvalidZipPath_ReturnsErrorNotThrows()
    {
        var options = new ExtractOptions
        {
            ArchivePaths = [@"C:\fake\archive.zip"],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
    }
}
```

---

## Model Tests — ArchiveOptionsTests.cs

```csharp
namespace Archiver.Core.Tests.Models;

public sealed class ArchiveOptionsTests
{
    [Fact]
    public void ArchiveOptions_Defaults_AreCorrect()
    {
        var options = new ArchiveOptions();

        options.SourcePaths.Should().BeEmpty();
        options.DestinationFolder.Should().Be(string.Empty);
        options.ArchiveName.Should().BeNull();
        options.Mode.Should().Be(ArchiveMode.SingleArchive);
        options.OnConflict.Should().Be(ConflictBehavior.Ask);
        options.OpenDestinationFolder.Should().BeFalse();
        options.DeleteSourceFiles.Should().BeFalse();
    }

    [Fact]
    public void ExtractOptions_Defaults_AreCorrect()
    {
        var options = new ExtractOptions();

        options.ArchivePaths.Should().BeEmpty();
        options.Mode.Should().Be(ExtractMode.SeparateFolders);
        options.DeleteArchiveAfterExtraction.Should().BeFalse();
    }
}
```

---

## Acceptance Criteria for Tests (T-12)

- [ ] `dotnet test` passes with zero failures
- [ ] All 10 test cases above are implemented
- [ ] No tests use `Thread.Sleep` — use `await Task.Delay` if needed
- [ ] Each test cleans up temp files via `TempDirectory.Dispose()`
- [ ] No test depends on another test's state
