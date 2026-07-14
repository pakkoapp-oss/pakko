using System.Net;
using System.Net.Sockets;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// The three real sandbox-behavior proofs T-F52's acceptance criteria require, targeting the
/// launcher/Job-Object/AppContainer mechanism generically rather than tar.exe itself (Phase 0
/// already confirmed tar.exe never spawns child processes for any format Pakko supports, so a
/// tar.exe-specific child-spawn test would prove nothing real). See DECISIONS.md's T-F52 entries
/// for the full empirical trail these mechanisms were built against.
/// </summary>
public sealed class TarSandboxedServiceSandboxBehaviorTests
{
    private const string CmdExecutablePath = @"C:\Windows\System32\cmd.exe";
    private const string CurlExecutablePath = @"C:\Windows\System32\curl.exe";

    [Fact]
    public async Task RunAsync_FileWriteOutsideQuarantine_FailsWithAccessDenied()
    {
        // Mirrors Phase 0's own negative control (see DECISIONS.md) and QuarantineAclTests'
        // lower-level version of the same proof, exercised here through the same primitives
        // TarSandboxedService itself uses: a destination Pakko created but never granted an ACE
        // to must be unreachable to the sandboxed process, even though Pakko's own identity can
        // read/write it freely.
        var profile = new AppContainerProfile("Pakko.TarSandbox.Test." + Guid.NewGuid());
        try
        {
            profile.EnsureExists();
            using var sid = profile.GetSid();
            using var securityCapabilities = SecurityCapabilitiesAttributeList.Create(sid);

            string neverAcldDir = Path.Combine(Path.GetTempPath(), "PakkoSandboxBehaviorTest_" + Guid.NewGuid());
            Directory.CreateDirectory(neverAcldDir);
            try
            {
                string targetFile = Path.Combine(neverAcldDir, "should_not_exist.txt");

                var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
                    CmdExecutablePath,
                    ["/c", "echo blocked > " + targetFile],
                    securityCapabilities.AttributeList,
                    jobObject: null,
                    CancellationToken.None);

                exitCode.Should().NotBe(0);
                File.Exists(targetFile).Should().BeFalse();
            }
            finally
            {
                Directory.Delete(neverAcldDir, recursive: true);
            }
        }
        finally
        {
            try { profile.Delete(); } catch { }
        }
    }

    [Fact]
    public async Task RunAsync_ChildProcessSpawnAttempt_NeverCompletesUnderJobObjectActiveProcessLimit()
    {
        // Job Object ActiveProcessLimit = 1 is the launcher/Job-Object mechanism's own defense
        // against a post-exploit second stage (spawning cmd.exe/powershell.exe/etc.) — proven
        // here generically (plain cmd.exe under the same job), not against tar.exe itself.
        using SandboxJobObject job = SandboxJobObject.Create(
            ramLimitBytes: 512L * 1024 * 1024, cpuTimeLimit: TimeSpan.FromSeconds(30));

        var (_, stdOut, _) = await SandboxedProcessLauncher.RunAsync(
            CmdExecutablePath,
            ["/c", "cmd /c \"exit 0\" && echo CHILD_COMPLETED"],
            attributeList: null,
            job.Handle,
            CancellationToken.None);

        // The nested cmd.exe is a second active process under the same job — either its
        // CreateProcess call fails outright, or the job terminates the violation before it can
        // finish — either way "CHILD_COMPLETED" (only printed after the nested process's own
        // exit code is observed by the outer shell) must never appear.
        stdOut.Should().NotContain("CHILD_COMPLETED");
    }

    [Fact]
    public async Task RunAsync_SocketConnectAttempt_FailsInsideAppContainerButSucceedsUnsandboxed()
    {
        if (!File.Exists(CurlExecutablePath))
            return; // curl.exe ships with Windows 10 1803+/11; skip cleanly if genuinely absent.

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        int acceptedConnectionCount = 0;
        using var cts = new CancellationTokenSource();
        // Accepted clients are disposed only after both curl calls finish (see the finally block
        // below) — disposing a TcpClient immediately after WriteAsync races the OS's delivery of
        // that data under load (parallel test execution) and can send an abrupt RST instead of a
        // graceful close, which curl reports as CURLE_RECV_ERROR (56) — a real flake found by
        // running the full suite in parallel, not visible when this test ran in isolation.
        var acceptedClients = new System.Collections.Concurrent.ConcurrentBag<TcpClient>();

        // A real TCP handshake can complete (and curl will report "Established connection") even
        // before .NET's AcceptTcpClientAsync is called — the OS itself queues the pending SYN in
        // the backlog. Without ever answering with an HTTP response, curl blocks until its own
        // -m timeout and reports failure regardless of whether the connection was reachable at
        // all — a false negative unrelated to AppContainer's capability model (found empirically:
        // an earlier draft of this test that never answered the socket made even the unsandboxed
        // curl "fail" this way). This background loop answers every real connection with a
        // minimal 200 OK so a REACHABLE curl always succeeds, leaving connect-level unreachability
        // (never even establishing a connection) as the only way the sandboxed case can fail.
        var serverLoopTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(cts.Token);
                    acceptedClients.Add(client);
                    Interlocked.Increment(ref acceptedConnectionCount);
                    NetworkStream stream = client.GetStream();
                    byte[] response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
                    await stream.WriteAsync(response, cts.Token);
                    await stream.FlushAsync(cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });

        var profile = new AppContainerProfile("Pakko.TarSandbox.Test." + Guid.NewGuid());
        try
        {
            profile.EnsureExists();
            using var sid = profile.GetSid();
            using var securityCapabilities = SecurityCapabilitiesAttributeList.Create(sid);

            var (sandboxedExitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
                CurlExecutablePath,
                ["-s", "-m", "3", $"http://127.0.0.1:{port}/"],
                securityCapabilities.AttributeList,
                jobObject: null,
                CancellationToken.None);

            sandboxedExitCode.Should().NotBe(0,
                "the AppContainer has no internetClient capability, so curl must fail to connect");

            // Same listener, unsandboxed launch of the same command — attributes the failure
            // above to the missing capability specifically, not environment/listener flakiness.
            var (unsandboxedExitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
                CurlExecutablePath,
                ["-s", "-m", "3", $"http://127.0.0.1:{port}/"],
                attributeList: null,
                jobObject: null,
                CancellationToken.None);

            unsandboxedExitCode.Should().Be(0);
            Volatile.Read(ref acceptedConnectionCount).Should().Be(1,
                "only the unsandboxed curl should have ever reached the listener");
        }
        finally
        {
            cts.Cancel();
            foreach (TcpClient client in acceptedClients)
                client.Dispose();
            try { profile.Delete(); } catch { }
            listener.Stop();
            try { await serverLoopTask; } catch { }
        }
    }
}
