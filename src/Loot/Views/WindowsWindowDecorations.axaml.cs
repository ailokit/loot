using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Loot.Views;

/// <summary>Windows-only Avalonia 12 decorations; other platforms retain LootWindowChrome.</summary>
internal sealed partial class WindowsWindowDecorations : ResourceDictionary
{
    public WindowsWindowDecorations() => AvaloniaXamlLoader.Load(this);
}

// A Control cannot be used directly as a style Setter value. Keep the window
// reference in a plain context object, with compiled bindings for AOT builds.
internal sealed record WindowsWindowDecorationsContext(LootWindow Window);
