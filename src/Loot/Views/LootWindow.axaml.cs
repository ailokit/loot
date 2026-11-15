using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace Loot.Views;

/// <summary>
/// Base window with cross-platform title-bar behavior, native Windows 11 chrome enhancements,
/// and DI scope management. Each window owns a dedicated <see cref="IServiceScope"/> that is
/// disposed when the window closes, guaranteeing transient resources are cleaned up promptly.
/// </summary>
public abstract class LootWindow : Avalonia.Controls.Window
{
    public static readonly StyledProperty<Control?> CustomTitleBarButtonsProperty =
        AvaloniaProperty.Register<LootWindow, Control?>(nameof(CustomTitleBarButtons));

    private readonly LootWindowChrome? _chrome;
    private readonly ContentControl _contentHost;
    private bool _routingContent;

    protected LootWindow()
    {
        ExtendClientAreaToDecorationsHint = true;
        if (OperatingSystem.IsWindows())
        {
            // Full keeps WS_CAPTION/WS_THICKFRAME under Avalonia's control across
            // state, capability and DPI changes. The decorations theme replaces
            // the title bar visually without removing native window behavior.
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaTitleBarHeightHint = 28;
            var resources = new WindowsWindowDecorations();
            WindowDecorationsTheme = new ControlTheme(typeof(WindowDrawnDecorations))
            {
                BasedOn = (ControlTheme)resources["LootWindowsWindowDecorations"]!,
                Setters = { new Setter(StyledElement.DataContextProperty, new WindowsWindowDecorationsContext(this)) }
            };
            Win32Properties.SetWindowCornerPreference(this, Win32Properties.WindowCornerPreference.Round);
            _contentHost = new ContentControl
            {
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
            // Avalonia supplies the effective title bar inset, including fullscreen
            // and scaling changes. Keep client content below the decorations overlay.
            _contentHost.Bind(MarginProperty, this.GetObservable(WindowDecorationMarginProperty));
        }
        else
        {
            WindowDecorations = WindowDecorations.None;
            _chrome = new LootWindowChrome();
            _chrome.CustomTitleBarButtonsHost.Content = CustomTitleBarButtons;
            _contentHost = _chrome;
        }
        Content = _contentHost;
    }

    /// <summary>Gets or sets controls displayed before the standard window buttons.</summary>
    public Control? CustomTitleBarButtons
    {
        get => GetValue(CustomTitleBarButtonsProperty);
        set => SetValue(CustomTitleBarButtonsProperty, value);
    }

    /// <summary>The DI scope that owns this window and all its transient dependencies.</summary>
    protected IServiceScope? Scope { get; private set; }

    /// <summary>
    /// Sets the DI scope that owns this window. Called by <see cref="Services.WindowNavigationService"/>
    /// immediately after the window is resolved from the scope. The scope is disposed when the window
    /// closes, releasing all transient resources.
    /// </summary>
    public void SetScope(IServiceScope scope) => Scope = scope;

    /// <summary>Gets whether a close request should hide the window unless the application is exiting.</summary>
    protected virtual bool HideOnClose => true;

    private void ConfigurePlatformTitleBar()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
        _chrome!.StandardWindowButtons.IsVisible = false;
        _chrome.TitleText.Margin = new Thickness(72, 0, 12, 0);
    }

    private void OnCloseWindow(object? sender, RoutedEventArgs e)
    {
        if (HideOnClose)
        {
            Hide();
            return;
        }
        Close();
    }

    internal void MinimizeFromTitleBar() => WindowState = WindowState.Minimized;

    internal void ToggleMaximizeFromTitleBar() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    internal void CloseFromTitleBar() => OnCloseWindow(this, new RoutedEventArgs());

    private void UpdateMaximizeIcon() => _chrome?.UpdateMaximizeIcon(WindowState);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CustomTitleBarButtonsProperty)
        {
            if (_chrome is not null)
                _chrome.CustomTitleBarButtonsHost.Content = CustomTitleBarButtons;
        }
        else if (change.Property == ContentProperty && _contentHost is not null && !_routingContent && !ReferenceEquals(Content, _contentHost))
        {
            var childContent = Content;
            _routingContent = true;
            try
            {
                Content = _contentHost;
                if (_chrome is not null)
                    _chrome.WindowContentPresenter.Content = childContent;
                else
                    _contentHost.Content = childContent;
            }
            finally { _routingContent = false; }
        }
        else if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeIcon();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Dispose the ViewModel (DataContext) before the scope so that event
        // unsubscriptions and collection clearing happen while DI is intact.
        if (DataContext is System.IDisposable disposable)
        {
            disposable.Dispose();
            DataContext = null;
        }
        DisposeScope();
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (HideOnClose && Application.Current is not App { IsExiting: true })
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Disposes the dedicated DI scope, releasing all transient resources.</summary>
    private void DisposeScope()
    {
        Scope?.Dispose();
        Scope = null;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ConfigurePlatformTitleBar();
        UpdateMaximizeIcon();
    }
}

internal partial class LootWindowChrome : UserControl
{
    public LootWindowChrome() => InitializeComponent();

    public void UpdateMaximizeIcon(WindowState state) =>
        MaximizeIcon.Kind = state == WindowState.Maximized ? MaterialIconKind.WindowRestore : MaterialIconKind.WindowMaximize;

    private void OnMinimizeWindow(object? sender, RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as LootWindow)?.MinimizeFromTitleBar();

    private void OnToggleMaximizeWindow(object? sender, RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as LootWindow)?.ToggleMaximizeFromTitleBar();

    private void OnCloseWindow(object? sender, RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as LootWindow)?.CloseFromTitleBar();
}
