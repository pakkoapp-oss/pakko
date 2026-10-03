using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F288: the two COM objects Shell uses are created through source-generated interfaces
// ([GeneratedComInterface]) instead of [ComImport]. A wrong vtable order or a lost [PreserveSig]
// shows up only at run time — HasUserCancelled read back false forever when [PreserveSig] was
// missing (CLAUDE.md) — so these call real methods on the real objects, without showing a window.
public sealed class ShellComTests
{
    [Fact]
    public void ProgressDialog_BeforeStart_TitleSetsAndNotCancelled()
    {
        IProgressDialog dialog = ShellCom.Create<IProgressDialog>(NativeProgressDialog.ClassId);
        try
        {
            dialog.SetTitle("Pakko test");
            dialog.SetLine(1, "line", false, IntPtr.Zero);
            dialog.HasUserCancelled().Should().BeFalse();
        }
        finally
        {
            ShellCom.Release(dialog);
        }
    }

    // A package that does not exist: the call must come back with a failure HRESULT (the
    // [PreserveSig] slot), not throw and not activate anything.
    [Fact]
    public async Task ActivationManager_UnknownApp_ReturnsFailureHResult()
    {
        int hr = await Task.Run(() =>
        {
            IApplicationActivationManager manager = ShellCom.Create<IApplicationActivationManager>(AppLauncher.ActivationManagerClassId);
            try
            {
                return manager.ActivateApplication("Pakko.NoSuchPackage_0000000000000!App", "", 0, out _);
            }
            finally
            {
                ShellCom.Release(manager);
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));

        hr.Should().BeLessThan(0);
    }

    [Fact]
    public void Create_UnregisteredClass_ThrowsComException()
    {
        Action act = () => ShellCom.Create<IProgressDialog>(Guid.Parse("00000000-0000-0000-0000-0000000000ab"));

        act.Should().Throw<System.Runtime.InteropServices.COMException>();
    }
}
