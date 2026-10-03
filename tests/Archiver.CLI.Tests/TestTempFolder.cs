namespace Archiver.CLI.Tests;

/// <summary>
/// T-F300: a per-test %TEMP% folder that is really removed. A tar leg runs through the sandbox, and
/// tar.exe or the AppContainer cleanup can still hold a file for a moment after the test ends, so a
/// single delete attempt left 133 folders behind. Retries for up to about five seconds. Not
/// IAsyncDisposable: xUnit 2.5 does not call that on a test class, so the class deletes it from
/// IAsyncLifetime.
/// </summary>
internal sealed class TestTempFolder
{
    private const int Attempts = 50;

    public TestTempFolder(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;

    public string Path { get; }

    public async Task DeleteAsync()
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                await Task.Delay(100);
            }
        }
    }
}
