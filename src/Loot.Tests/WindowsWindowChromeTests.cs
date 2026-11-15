using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Loot.Views;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Loot.Tests;

public sealed class WindowsWindowChromeTests
{
    // These assertions need a real HWND and the Win32 backend. A headless
    // platform cannot verify the non-client hit test used by Windows Snap.
    [WindowsFact]
    public async Task CustomDecorations_PreserveNativeHitTestsAndStylesAcrossStateChanges()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<ChromeTestApplication>().UsePlatformDetect().SetupWithoutStarting();
                VerifyNativeChrome();
                result.SetResult();
            }
            catch (Exception exception)
            {
                result.SetException(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void VerifyNativeChrome()
    {
        var context = new object();
        var customButton = new Button { Content = "Custom", Width = 60 };
        var content = new Border();
        var window = new ChromeTestWindow
        {
            Width = 520,
            Height = 400,
            Title = "Chrome test",
            DataContext = context,
            CustomTitleBarButtons = customButton,
            Content = content
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var handle = window.TryGetPlatformHandle()!.Handle;
            var root = window.GetVisualAncestors().Last();
            var buttons = root.GetVisualDescendants().OfType<Button>().ToList();
            var maximize = Assert.Single(buttons, button => button.Name == "PART_MaximizeButton");
            var minimize = Assert.Single(buttons, button => button.Name == "PART_MinimizeButton");
            var close = Assert.Single(buttons, button => button.Name == "PART_CloseButton");
            var maximizeIcon = root.GetVisualDescendants().OfType<MaterialIcon>().Single(icon => icon.Name == "MaximizeIcon");

            Assert.Same(context, customButton.DataContext);
            Assert.Same(window, customButton.GetLogicalAncestors().OfType<LootWindow>().Single());
            Assert.Equal(9, HitTest(handle, maximize)); // HTMAXBUTTON: Snap Layouts
            Assert.Equal(8, HitTest(handle, minimize)); // HTMINBUTTON
            Assert.Equal(20, HitTest(handle, close)); // HTCLOSE
            Assert.Equal(1, HitTest(handle, customButton)); // HTCLIENT
            Assert.Equal(2, HitTest(handle, window, new Point(80, 16))); // HTCAPTION
            Assert.True(content.TranslatePoint(default, window)!.Value.Y >= window.WindowDecorationMargin.Top);
            AssertNativeStyles(handle);
            Assert.Equal(MaterialIconKind.WindowMaximize, maximizeIcon.Kind);

            // Exercise Avalonia's standard template PART handlers, the same path
            // reached after it redirects native caption-button pointer input.
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(MaterialIconKind.WindowRestore, maximizeIcon.Kind);
            AssertNativeStyles(handle);
            Assert.Equal(9, HitTest(handle, maximize));

            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MaterialIconKind.WindowMaximize, maximizeIcon.Kind);
            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(WindowState.Minimized, window.WindowState);
            AssertNativeStyles(handle);
            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            AssertNativeStyles(handle);

            window.CanResize = false;
            window.CanMinimize = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(maximize.IsVisible);
            Assert.False(minimize.IsVisible);
            Assert.Equal(0, GetWindowLong(handle, -16) & 0x00070000); // no resize/min/max boxes

            window.CanResize = true;
            window.CanMinimize = true;
            Dispatcher.UIThread.RunJobs();
            AssertNativeStyles(handle);

            // A normal close goes through LootWindow.OnClosing so HideOnClose
            // still works for the framework-owned close button.
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(9, HitTest(handle, maximize));
            AssertNativeStyles(handle);
        }
        finally
        {
            window.DisposeOnClose = true;
            window.Close();
        }
    }

    private static void AssertNativeStyles(nint handle)
    {
        const int expected = 0x00C00000 | 0x00080000 | 0x00040000 | 0x00020000 | 0x00010000;
        Assert.Equal(expected, GetWindowLong(handle, -16) & expected);
    }

    private static int HitTest(nint handle, Control control) =>
        HitTest(handle, control, new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));

    private static int HitTest(nint handle, Control control, Point point)
    {
        var screen = control.PointToScreen(point);
        var coordinates = unchecked((nint)((screen.X & 0xffff) | ((screen.Y & 0xffff) << 16)));
        return (int)SendMessage(handle, 0x0084, 0, coordinates); // WM_NCHITTEST
    }

    private sealed class ChromeTestWindow : LootWindow
    {
        public bool DisposeOnClose { get; set; }
        protected override bool HideOnClose => !DisposeOnClose;
    }

    private sealed class ChromeTestApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            Styles.Add(new Style(selector => selector.OfType<Button>().Class("window-action-button"))
            {
                Setters = { new Setter(Layoutable.WidthProperty, 36d), new Setter(Layoutable.HeightProperty, 26d) }
            });
        }
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Requires the Windows desktop backend and a real HWND.";
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint handle, uint message, nint wParam, nint lParam);
}
