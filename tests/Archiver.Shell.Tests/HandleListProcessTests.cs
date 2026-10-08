using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F356: the helper may now start in the middle of an operation, so it must not take along
// handles that belong to another child (tar.exe's pipes). A real child process on real pipes.
public sealed partial class HandleListProcessTests
{
    private static readonly string Ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
    private static readonly string[] FiveSeconds = ["-n", "6", "127.0.0.1"];

    [Fact]
    public async Task Start_ChildDoesNotInheritAnInheritableHandleItWasNotGiven()
    {
        // Someone else's pipe, inheritable at the moment the child starts - as tar.exe's are.
        using var others = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var own = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);

        using var child = HandleListProcess.Start(Ping, FiveSeconds, [own.ClientSafePipeHandle]);
        try
        {
            others.DisposeLocalCopyOfClientHandle();

            // With every writer gone the read ends; a child holding the write end would keep it open.
            int read = await others.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            read.Should().Be(0);
            Process.GetProcessById(child.Id).HasExited.Should().BeFalse("the child is still running, so it is not what closed the pipe");
        }
        finally
        {
            child.Kill();
        }
    }

    [Fact]
    public async Task Start_ChildInheritsTheHandleItWasGiven()
    {
        using var own = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        using var child = HandleListProcess.Start(Ping, FiveSeconds, [own.ClientSafePipeHandle]);
        try
        {
            own.DisposeLocalCopyOfClientHandle();

            Task<int> read = own.ReadAsync(new byte[1]).AsTask();
            await Task.Delay(300);
            read.IsCompleted.Should().BeFalse("the child holds the write end");

            child.Kill();
            (await read.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(0);
        }
        finally
        {
            child.Kill();
        }
    }

    [Fact]
    public void Kill_Twice_AndAfterDispose_DoesNotThrow()
    {
        using var own = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var child = HandleListProcess.Start(Ping, FiveSeconds, [own.ClientSafePipeHandle]);

        child.Kill();
        child.Kill();
        child.Dispose();
        Action afterDispose = child.Kill;

        afterDispose.Should().NotThrow();
        bool gone;
        try
        {
            gone = Process.GetProcessById(child.Id).WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            gone = true; // no longer found by its id
        }
        gone.Should().BeTrue();
    }

    [Fact]
    public void Start_ExeThatDoesNotExist_ThrowsWin32Exception()
    {
        using var own = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);

        Action act = () => HandleListProcess.Start(@"C:\no-such-folder-pakko\nothing.exe", [], [own.ClientSafePipeHandle]);

        act.Should().Throw<Win32Exception>();
    }

    [Theory]
    [InlineData(new[] { "--in", "1234" }, "\"C:\\a b\\x.exe\" \"--in\" \"1234\"")]
    [InlineData(new[] { "a\"b" }, "\"C:\\a b\\x.exe\" \"a\\\"b\"")]
    [InlineData(new[] { "end\\" }, "\"C:\\a b\\x.exe\" \"end\\\\\"")]
    [InlineData(new[] { "mid\\dle", "" }, "\"C:\\a b\\x.exe\" \"mid\\dle\" \"\"")]
    public void BuildCommandLine_QuotesTheWayCommandLineToArgvReadsBack(string[] arguments, string expected)
    {
        string line = HandleListProcess.BuildCommandLine(@"C:\a b\x.exe", arguments);

        line.Should().Be(expected);
        SplitWithWindows(line).Skip(1).Should().Equal(arguments);
    }

    private static string[] SplitWithWindows(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW(commandLine, out int count);
        try
        {
            return [.. Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!)];
        }
        finally
        {
            _ = LocalFree(argv);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
