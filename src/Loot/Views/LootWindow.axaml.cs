using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Loot;
using Loot.Logging;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

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

    private const int WindowStyleIndex = -16;
    private const long WindowStyleCaption = 0x00C00000L;
    private const long WindowStyleSystemMenu = 0x00080000L;
    private const long WindowStyleMinimizeBox = 0x00020000L;
    private const long WindowStyleMaximizeBox = 0x00010000L;
    private const uint SetWindowPosFrameChanged = 0x0020;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    private readonly LootWindowChrome _chrome;
    private bool _routingContent;

    protected LootWindow()
    {
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        _chrome = new LootWindowChrome();
        _chrome.CustomTitleBarButtonsHost.Content = CustomTitleBarButtons;
        Content = _chrome;
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
        _chrome.StandardWindowButtons.IsVisible = false;
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

    private void UpdateMaximizeIcon() => _chrome.UpdateMaximizeIcon(WindowState);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CustomTitleBarButtonsProperty)
        {
            _chrome.CustomTitleBarButtonsHost.Content = CustomTitleBarButtons;
        }
        else if (change.Property == ContentProperty && !_routingContent && !ReferenceEquals(Content, _chrome))
        {
            var childContent = Content;
            _routingContent = true;
            try
            {
                Content = _chrome;
                _chrome.WindowContentPresenter.Content = childContent;
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
        try { EnableNativeWindowChrome(); }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(LootWindow), "Could not configure native window chrome", LogLevel.Warning);
        }
    }

    private void EnableNativeWindowChrome()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { Handle: var handle })
        {
            return;
        }

        EnableWindowsStateAnimations(handle);

        // Keep the client-drawn title bar while asking DWM to render the outer
        // frame. DWM then owns the shadow, DPI scaling and Windows 11 corners.
        var margins = new Margins(1, 1, 1, 1);
        _ = DwmExtendFrameIntoClientArea(handle, in margins);

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            in cornerPreference,
            sizeof(int));
    }

    private static void EnableWindowsStateAnimations(nint handle)
    {
        // Avalonia's WindowDecorations=None removes these non-client style bits.
        // The client still owns the title bar and buttons, but Windows needs the
        // bits present to associate the window with the normal DWM state
        // transitions (minimize, maximize, restore and close).
        var style = GetWindowLongPtr(handle, WindowStyleIndex).ToInt64();
        if (style == 0)
        {
            return;
        }

        var animatedStyle = style
            | WindowStyleCaption
            | WindowStyleSystemMenu
            | WindowStyleMinimizeBox
            | WindowStyleMaximizeBox;

        if (style == animatedStyle)
        {
            return;
        }

        if (SetWindowLongPtr(handle, WindowStyleIndex, new nint(animatedStyle)) == nint.Zero
            && Marshal.GetLastWin32Error() != 0)
        {
            return;
        }

        _ = SetWindowPos(
            handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            SetWindowPosFrameChanged
                | SetWindowPosNoActivate
                | SetWindowPosNoMove
                | SetWindowPosNoSize
                | SetWindowPosNoZOrder);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Margins(int Left, int Right, int Top, int Bottom);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint windowHandle, in Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        in int attributeValue,
        int attributeSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
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
