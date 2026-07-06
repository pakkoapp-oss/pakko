using System.Diagnostics;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Extracts tar-family archives (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma, 7z, rar) via
/// the system's tar.exe. The extraction pipeline lands in T-F49.
/// </summary>
public sealed class TarProcessService : ITarService
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    // DetectCapabilitiesAsync runs synchronously on app startup (App.xaml.cs forces eager
    // resolution) — a hung tar.exe --version must not hang app launch indefinitely.
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public async Task<TarCapabilities> DetectCapabilitiesAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = TarExecutablePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
                return new TarCapabilities();

            using var timeoutCts = new CancellationTokenSource(DetectionTimeout);

            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                return TarVersionParser.Parse(output);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new TarCapabilities();
            }
        }
        catch (Exception)
        {
            return new TarCapabilities();
        }
    }

    /// <inheritdoc/>
    public Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Tar extraction pipeline lands in T-F49.");
    }
}
