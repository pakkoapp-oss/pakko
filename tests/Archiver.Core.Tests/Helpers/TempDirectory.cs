namespace Archiver.Core.Tests.Helpers;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        System.IO.Path.GetRandomFileName());

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string CreateFile(string name, string content = "test content")
    {
        string path = System.IO.Path.Combine(Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    // A full parallel run showed a freshly written file still held for a few milliseconds by
    // another process after the test closed it (Restart Manager listed no holder a moment later),
    // so cleanup retries briefly instead of failing a test whose assertions all passed.
    public void Dispose()
    {
        using var pause = new ManualResetEventSlim();
        for (int attempt = 1; Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) when (attempt < 20)
            {
                pause.Wait(TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
