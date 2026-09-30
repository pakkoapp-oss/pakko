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
/// A conflict, password (step 5) or yes/no (T-F217) prompt replaces the progress part until answered.
/// </summary>
internal sealed partial class OperationWindow
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
    private readonly FontFamily _monospaceFont = new("Cascadia Mono, Consolas");
    private readonly FontFamily _proportionalFont;
    private readonly Button _cancel = new() { MinWidth = 120 };
    private readonly Button _close = new() { MinWidth = 120 };

    private readonly StackPanel _conflictPanel = new() { Spacing = 8 };
    private readonly TextBlock _conflictPath = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _existingLabel = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _existingDetails = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _incomingLabel = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _incomingDetails = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _applyToAll = new();
    private readonly Button _overwrite = new() { MinWidth = 100 };
    private readonly Button _rename = new() { MinWidth = 100 };
    private readonly Button _skip = new() { MinWidth = 100 };

    private readonly StackPanel _passwordPanel = new() { Spacing = 8 };
    private readonly TextBlock _passwordMessage = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };

    // No MaxLength: a long password is never cut (T-F255).
    private readonly PasswordBox _passwordBox = new();
    private readonly TextBlock _wrongPassword = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _applyToRemaining = new();
    private readonly Button _passwordOk = new() { MinWidth = 120 };
    private readonly Button _skipArchive = new() { MinWidth = 120 };

    // T-F217: a yes/no question; every text comes with it from Shell.
    private readonly TextBlock _confirmMessage = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly Button _confirm = new() { MinWidth = 120 };
    private readonly Button _decline = new() { MinWidth = 120 };
    private ProtocolMessage? _renderedPrompt;
    private bool _renderedResult;
    private DispatcherQueueTimer? _showTimer;
    private bool _closing;

    public OperationWindow(OperationWindowModel model, Action<WindowUpdate> execute)
    {
        _model = model;
        _execute = execute;
        _proportionalFont = _resultText.FontFamily;
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
        ProtocolMessage? prompt = result ? null : _model.Prompt;
        bool progress = !result && prompt is null;
        SetVisible(_itemLine, !result && _model.ItemLine is not null);
        SetVisible(_bar, progress);
        SetVisible(_file, progress && !string.IsNullOrEmpty(_model.CurrentFile));
        SetVisible(_status, progress && !string.IsNullOrEmpty(_model.Status));
        SetVisible(_cancel, progress);
        SetVisible(_conflictPanel, prompt is AskConflict);
        SetVisible(_overwrite, prompt is AskConflict);
        SetVisible(_rename, prompt is AskConflict);
        SetVisible(_skip, prompt is AskConflict);
        SetVisible(_passwordPanel, prompt is AskPassword);
        SetVisible(_passwordOk, prompt is AskPassword);
        SetVisible(_skipArchive, prompt is AskPassword);
        SetVisible(_confirmMessage, prompt is AskConfirm);
        SetVisible(_confirm, prompt is AskConfirm);
        SetVisible(_decline, prompt is AskConfirm);
        SetVisible(_resultScroll, result);
        SetVisible(_close, result);
        SetVisible(_severityIcon, result);

        if (result)
        {
            ResultText r = _model.Result!;
            _heading.Text = r.Title;
            _resultText.Text = r.Text;
            // A hash is 64-73 characters: no font fits it in the window's width, and a break
            // inside it makes it unreadable. The same controls show every other result wrapped.
            _resultText.FontFamily = r.Preformatted ? _monospaceFont : _proportionalFont;
            _resultText.TextWrapping = r.Preformatted ? TextWrapping.NoWrap : TextWrapping.Wrap;
            _resultScroll.HorizontalScrollMode = r.Preformatted ? ScrollMode.Enabled : ScrollMode.Disabled;
            _resultScroll.HorizontalScrollBarVisibility = r.Preformatted ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            (_severityIcon.Glyph, _severityIcon.Foreground) = r.Severity switch
            {
                ResultSeverity.Error => (ErrorGlyph, Brush("SystemFillColorCriticalBrush")),
                ResultSeverity.Warning => (WarningGlyph, Brush("SystemFillColorCautionBrush")),
                _ => (SuccessGlyph, Brush("SystemFillColorSuccessBrush")),
            };
            _close.Content = _model.CloseLabel;
        }
        else if (prompt is not null)
        {
            _itemLine.Text = _model.ItemLine ?? "";
            if (!ReferenceEquals(prompt, _renderedPrompt))
                RenderPrompt(prompt);
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

        // The view changed under the focused control (an answered prompt's button, or Cancel when
        // the result arrives): move focus to the new view's default so Enter and Esc keep working.
        bool viewChanged = (_renderedPrompt is not null && prompt is null) || (result && !_renderedResult);
        _renderedPrompt = prompt;
        _renderedResult = result;
        if (_window.AppWindow.IsVisible)
        {
            FitToContent();
            if (viewChanged)
                FocusDefault();
        }
    }

    /// <summary>A prompt came up while the window shows: bring it forward and focus the prompt.</summary>
    public void ActivatePrompt()
    {
        Render();
        _window.Activate();
        FocusDefault();
    }

    /// <summary>First show: size to the content, center on the work area, take the foreground.</summary>
    public void ShowNow()
    {
        Render();
        FitToContent();
        CenterOnWorkArea();
        _window.Activate();
        FocusDefault();
    }

    public void CloseNow()
    {
        _closing = true;
        _showTimer?.Stop();
        _window.Close();
    }

    // Skip is the conflict's default, as in the Win32 dialog (Enter never overwrites).
    private void FocusDefault()
    {
        Control target = _model.Prompt switch
        {
            _ when _close.Visibility == Visibility.Visible => _close,
            AskConflict => _skip,
            AskPassword => _passwordBox,
            AskConfirm => _decline,
            _ => _cancel,
        };
        target.Focus(FocusState.Programmatic);
    }

    // Only when a new prompt comes up, so progress refreshes never reset what the user is typing.
    private void RenderPrompt(ProtocolMessage prompt)
    {
        _passwordBox.Password = "";
        _applyToAll.IsChecked = false;
        _applyToRemaining.IsChecked = false;
        switch (prompt)
        {
            case AskConflict ask:
                _heading.Text = _model.Text(WindowStrings.ConflictTitle);
                _conflictPath.Text = ask.ExistingPath;
                _existingLabel.Text = _model.Text(WindowStrings.ExistingFile);
                _existingDetails.Text = ask.ExistingDetails ?? "";
                _incomingLabel.Text = _model.Text(WindowStrings.IncomingFile);
                _incomingDetails.Text = _model.IncomingDetails ?? "";
                SetVisible(_existingDetails, ask.ExistingDetails is not null);
                SetVisible(_incomingDetails, ask.IncomingDetails is not null);
                _applyToAll.Content = _model.Text(WindowStrings.ApplyToAll);
                _overwrite.Content = _model.Text(WindowStrings.Overwrite);
                _rename.Content = _model.Text(WindowStrings.Rename);
                _skip.Content = _model.Text(WindowStrings.Skip);
                break;

            case AskPassword ask:
                _heading.Text = _model.Text(WindowStrings.PasswordTitle);
                _passwordMessage.Text = _model.PasswordMessage ?? "";
                _passwordBox.Header = _model.Text(WindowStrings.PasswordLabel);
                _wrongPassword.Text = _model.Text(WindowStrings.WrongPassword);
                SetVisible(_wrongPassword, ask.PreviousAttemptWasWrong);
                _applyToRemaining.Content = _model.Text(WindowStrings.ApplyToRemaining);
                SetVisible(_applyToRemaining, ask.CanApplyToRemaining);
                _passwordOk.Content = _model.Text(WindowStrings.PasswordOk);
                _skipArchive.Content = _model.Text(WindowStrings.SkipArchive);
                break;

            case AskConfirm ask:
                _heading.Text = ask.Title;
                _confirmMessage.Text = ask.Message;
                _confirm.Content = ask.ConfirmLabel;
                _decline.Content = ask.DeclineLabel;
                break;
        }
    }

    // The typed password goes straight into the answer; the box is emptied before anything else runs.
    private void SubmitPassword()
    {
        string password = _passwordBox.Password;
        _passwordBox.Password = "";
        _execute(_model.SubmitPassword(password, _applyToRemaining.IsChecked == true));
    }

    private void BuildConflictPanel()
    {
        _conflictPanel.Children.Add(_conflictPath);
        _conflictPanel.Children.Add(Card(_existingLabel, _existingDetails));
        _conflictPanel.Children.Add(Card(_incomingLabel, _incomingDetails));
        _conflictPanel.Children.Add(_applyToAll);
        _overwrite.Click += (_, _) => _execute(_model.AnswerConflict(ConflictChoice.Overwrite, _applyToAll.IsChecked == true));
        _rename.Click += (_, _) => _execute(_model.AnswerConflict(ConflictChoice.Rename, _applyToAll.IsChecked == true));
        _skip.Click += (_, _) => _execute(_model.AnswerConflict(ConflictChoice.Skip, _applyToAll.IsChecked == true));
        _skip.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
    }

    private void BuildPasswordPanel()
    {
        _wrongPassword.Foreground = Brush("SystemFillColorCriticalBrush");
        _passwordPanel.Children.Add(_passwordMessage);
        _passwordPanel.Children.Add(_passwordBox);
        _passwordPanel.Children.Add(_wrongPassword);
        _passwordPanel.Children.Add(_applyToRemaining);
        _passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter)
                return;
            e.Handled = true;
            SubmitPassword();
        };
        _passwordOk.Click += (_, _) => SubmitPassword();
        _skipArchive.Click += (_, _) =>
        {
            _passwordBox.Password = "";
            _execute(_model.DeclinePassword());
        };
        _passwordOk.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
    }

    private static Border Card(TextBlock label, TextBlock details)
    {
        label.Foreground = Brush("TextFillColorSecondaryBrush");
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(label);
        content.Children.Add(details);
        return new Border
        {
            Child = content,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
        };
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
        BuildConflictPanel();
        BuildPasswordPanel();
        // Declining is the accent and the default: Enter never extracts a suspected bomb.
        _confirm.Click += (_, _) => _execute(_model.AnswerConfirm(true));
        _decline.Click += (_, _) => _execute(_model.AnswerConfirm(false));
        _decline.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        foreach (Button button in new[] { _overwrite, _rename, _skip, _passwordOk, _skipArchive, _confirm, _decline, _cancel, _close })
            buttons.Children.Add(button);

        var body = new StackPanel { Padding = new Thickness(24, 8, 24, 24), Spacing = 10 };
        foreach (UIElement element in new UIElement[] { heading, _itemLine, _bar, _file, _status, _conflictPanel, _passwordPanel, _confirmMessage, _resultScroll, buttons })
            body.Children.Add(element);
        Grid.SetRow(body, 1);
        _root.Children.Add(body);

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, e) =>
        {
            e.Handled = true;
            // An open prompt: Esc is its own "cancel" (Skip / no password), as in the Win32 dialogs.
            _passwordBox.Password = "";
            _execute(_model.Escape(_applyToAll.IsChecked == true));
        };
        _root.KeyboardAccelerators.Add(escape);
        // Accelerators on the root would otherwise pop an "Esc" tooltip over the window.
        _root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

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
            // With both off the caption shows only X, as a dialog's does (user decision).
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
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

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}
