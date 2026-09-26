using Archiver.OperationUi.Core;
using Archiver.OperationUi.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace Archiver.OperationUi;

/// <summary>
/// The code-only WinUI application: one hidden <see cref="OperationWindow"/>, driven by
/// <see cref="OperationWindowModel"/> from the messages Shell sends. With no XAML files the
/// metadata provider is the WinUI controls' own.
/// </summary>
internal sealed partial class HelperApp : Application, IXamlMetadataProvider, IDisposable
{
    private readonly XamlControlsXamlMetaDataProvider _provider = new();
    private readonly string _inHandle;
    private readonly string _outHandle;
    private readonly OperationWindowModel _model = new();
    private ShellPipe? _pipe;
    private OperationWindow? _window;

    public HelperApp(string inHandle, string outHandle)
    {
        _inHandle = inHandle;
        _outHandle = outHandle;
    }

    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();

    public void Dispose() => _pipe?.Dispose();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());

        _pipe = new ShellPipe(_inHandle, _outHandle);
        _window = new OperationWindow(_model, Execute);
        _pipe.Send(new HelperReady(FrameCodec.ProtocolVersion));

        var dispatcher = _window.DispatcherQueue;
        _ = Task.Run(() => _pipe.ReadAllAsync(message => dispatcher.TryEnqueue(() => OnMessage(message))));
    }

    private void OnMessage(ProtocolMessage? message)
    {
        if (message is null)
        {
            Execute(_model.ShellDisconnected());
            return;
        }

        if (message is Begin)
            _window!.StartShowTimer(OperationWindowModel.ShowDelay);
        Execute(_model.Receive(message));
    }

    private void Execute(WindowUpdate update)
    {
        foreach (ProtocolMessage message in update.Send)
            _pipe!.Send(message);

        switch (update.Command)
        {
            case WindowCommand.Refresh:
                _window!.Render();
                break;
            case WindowCommand.Show:
                _window!.ShowNow();
                break;
            case WindowCommand.Close:
                _window!.CloseNow();
                Dispose();
                Exit();
                break;
        }
    }
}
