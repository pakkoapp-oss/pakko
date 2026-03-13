namespace Archiver.ProgressWindow;

public partial class App : Microsoft.UI.Xaml.Application
{
    private ProgressWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Parse --pipe {name} and --title {text} from the raw command line.
        // Environment.GetCommandLineArgs()[0] is the exe path; args start at [1].
        var cmdArgs = Environment.GetCommandLineArgs();
        string pipeName = GetArgValue(cmdArgs, "--pipe") ?? string.Empty;
        string operationTitle = GetArgValue(cmdArgs, "--title") ?? "Pakko";

        _window = new ProgressWindow(pipeName, operationTitle);
        _window.Activate();
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
