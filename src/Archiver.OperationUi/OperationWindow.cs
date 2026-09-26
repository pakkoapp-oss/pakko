using System.Runtime.InteropServices;
using Archiver.OperationUi.Core;
using Archiver.OperationUi.Protocol;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.ApplicationModel.DataTransfer;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;
using Windows.UI;

namespace Archiver.OperationUi;

/// <summary>
/// The operation window, built in code (no XAML pages in this exe). It only renders
/// <see cref="OperationWindowModel"/>; every decision comes back from the model as a
/// <see cref="WindowUpdate"/> for <see cref="HelperApp"/> to carry out. Layout follows the
/// approved step 3 mockup: 520 px wide, title row, heading, archive line, bar, file, status, buttons.
/// </summary>
internal sealed class OperationWindow
{
    private const double WidthDip = 520;
    private const double TitleBarDip = 32;
    private const double MinHeightDip = 200;
    private const double MaxHeightDip = 560;

    // Segoe Fluent Icons code points, built from numbers so no private-use glyph sits in the source.
    private static readonly string SuccessGlyph = char.ConvertFromUtf32(0xE930);
    private static readonly string WarningGlyph = char.ConvertFromUtf32(0xE7BA);
    private static readonly string ErrorGlyph = char.ConvertFromUtf32(0xEA39);

    private readonly OperationWindowModel _model;
    private readonly Action<WindowUpdate> _execute;
    private readonly Window _window;
    private readonly Grid _root = new();
    private readonly TextBlock _titleBarText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Text = "Pakko" };
    private readonly FontIcon _severityIcon = new() { FontSize = 22, Visibility = Visibility.Collapsed };
    private readonly TextBlock _heading = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _itemLine = new() { FontSize = 14 };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _file = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _status = new() { FontSize = 12 };
    private readonly TextBlock _resultText = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly ScrollViewer _resultScroll = new() { MaxHeight = 300 };
    private readonly Button _cancel = new() { MinWidth = 120 };
    private readonly Button _close = new() { MinWidth = 120 };
    private DispatcherQueueTimer? _showTimer;
    private bool _closing;

    public OperationWindow(OperationWindowModel model, Action<WindowUpdate> execute)
    {
        _model = model;
        _execute = execute;
        _window = new Window { Content = _root };
        BuildLayout();
        ConfigureWindow();
    }

    public DispatcherQueue DispatcherQueue => _window.DispatcherQueue;

    public void StartShowTimer(TimeSpan delay)
    {
        _showTimer = _window.DispatcherQueue.CreateTimer();
        _showTimer.Interval = delay;
        _showTimer.IsRepeating = false;
        _showTimer.Tick += (_, _) => _execute(_model.ShowDelayElapsed());
        _showTimer.Start();
    }

    public void Render()
    {
        _window.Title = _model.Title;
        _root.FlowDirection = _model.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        bool result = _model.Phase == WindowPhase.Result && _model.Result is not null;
        SetVisible(_itemLine, !result && _model.ItemLine is not null);
        SetVisible(_bar, !result);
        SetVisible(_file, !result && !string.IsNullOrEmpty(_model.CurrentFile));
        SetVisible(_status, !result && !string.IsNullOrEmpty(_model.Status));
        SetVisible(_cancel, !result);
        SetVisible(_resultScroll, result);
        SetVisible(_close, result);
        SetVisible(_severityIcon, result);

        if (result)
        {
            ResultText r = _model.Result!;
            _heading.Text = r.Title;
            _resultText.Text = r.Text;
            (_severityIcon.Glyph, _severityIcon.Foreground) = r.Severity switch
            {
                ResultSeverity.Error => (ErrorGlyph, Brush("SystemFillColorCriticalBrush")),
                ResultSeverity.Warning => (WarningGlyph, Brush("SystemFillColorCautionBrush")),
                _ => (SuccessGlyph, Brush("SystemFillColorSuccessBrush")),
            };
            _close.Content = _model.CloseLabel;
        }
        else
        {
            _heading.Text = _model.Title;
            _itemLine.Text = _model.ItemLine ?? "";
            _bar.Value = _model.Percent;
            _file.Text = _model.CurrentFile ?? "";
            _status.Text = _model.Status ?? "";
            _cancel.Content = _model.CancelLabel;
            AutomationProperties.SetName(_bar, _model.ItemLine ?? _model.Title);
        }

        if (_window.AppWindow.IsVisible)
            FitToContent();
    }

    /// <summary>First show: size to the content, center on the work area, take the foreground.</summary>
    public void ShowNow()
    {
        Render();
        FitToContent();
        CenterOnWorkArea();
        _window.Activate();
        (_close.Visibility == Visibility.Visible ? _close : _cancel).Focus(FocusState.Programmatic);
    }

    public void CloseNow()
    {
        _closing = true;
        _showTimer?.Stop();
        _window.Close();
    }

    private void BuildLayout()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarDip) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new Grid { Padding = new Thickness(12, 0, 0, 0) };
        titleBar.Children.Add(_titleBarText);
        _root.Children.Add(titleBar);

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(_severityIcon);
        heading.Children.Add(_heading);

        _itemLine.Foreground = Brush("TextFillColorSecondaryBrush");
        _status.Foreground = Brush("TextFillColorSecondaryBrush");
        _resultScroll.Content = _resultText;
        _close.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _cancel.Click += (_, _) => _execute(_model.UserClosed());
        _close.Click += (_, _) => _execute(_model.UserClosed());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_close);

        var body = new StackPanel { Padding = new Thickness(24, 8, 24, 24), Spacing = 10 };
        foreach (UIElement element in new UIElement[] { heading, _itemLine, _bar, _file, _status, _resultScroll, buttons })
            body.Children.Add(element);
        Grid.SetRow(body, 1);
        _root.Children.Add(body);

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, e) =>
        {
            e.Handled = true;
            _execute(_model.UserClosed());
        };
        _root.KeyboardAccelerators.Add(escape);

        // MessageBoxW copied its text on Ctrl+C; users copy hashes that way.
        var copy = new KeyboardAccelerator { Key = VirtualKey.C, Modifiers = VirtualKeyModifiers.Control };
        copy.Invoked += (_, e) =>
        {
            // A selection inside the result text copies only what the user selected.
            if (_model.CopyText is not { } text || !string.IsNullOrEmpty(_resultText.SelectedText))
                return;
            e.Handled = true;
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        };
        _root.KeyboardAccelerators.Add(copy);

        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(titleBar);
    }

    private void ConfigureWindow()
    {
        AppWindow appWindow = _window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        // Mica where Windows supports it (11); the theme's solid background elsewhere.
        if (MicaController.IsSupported())
            _window.SystemBackdrop = new MicaBackdrop();
        else
            _root.Background = Brush("SolidBackgroundFillColorBaseBrush");

        // Content extends into the title bar, so its caption buttons follow the theme by hand.
        // ActualTheme is only settled once the content has loaded (the window loads while hidden).
        ApplyCaptionColors();
        _root.Loaded += (_, _) => ApplyCaptionColors();
        _root.ActualThemeChanged += (_, _) => ApplyCaptionColors();

        // The title bar's X is the same as Cancel while the operation runs (T-F269), and Close after.
        appWindow.Closing += (_, e) =>
        {
            if (_closing)
                return;
            e.Cancel = true;
            _execute(_model.UserClosed());
        };
    }

    private void ApplyCaptionColors()
    {
        AppWindowTitleBar titleBar = _window.AppWindow.TitleBar;
        Color foreground = _root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void FitToContent()
    {
        _root.Measure(new Size(WidthDip, double.PositiveInfinity));
        double heightDip = Math.Clamp(_root.DesiredSize.Height, MinHeightDip, MaxHeightDip);
        double scale = Scale();
        _window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(WidthDip * scale), (int)Math.Round(heightDip * scale)));
    }

    private void CenterOnWorkArea()
    {
        AppWindow appWindow = _window.AppWindow;
        RectInt32 area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        SizeInt32 size = appWindow.Size;
        appWindow.Move(new PointInt32(area.X + ((area.Width - size.Width) / 2), area.Y + ((area.Height - size.Height) / 2)));
    }

    // AppWindow sizes are physical pixels; the layout is in DIPs.
    private double Scale()
    {
        uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window));
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static void SetVisible(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
