using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Loot.JsonRender.Avalonia;

/// <summary>Registers text and small visual-content components.</summary>
internal static class ContentComponents
{
    public static void Register(JsonRenderComponentRegistry registry)
    {
        RegisterMany(registry, ["Text", "Label"], Text);
        registry.Register("Icon", Icon);
        RegisterMany(registry, ["Badge", "Tag"], Badge);
        registry.Register("Avatar", Avatar);
        registry.Register("Metric", Metric);
    }

    private static Control Text(JsonRenderComponentContext context)
    {
        var variant = context.GetString("variant", "body");
        var block = new TextBlock
        {
            Text = context.GetString("content", context.GetString("text")),
            TextWrapping = TextWrapping.Wrap,
            FontSize = variant switch { "title" => 22, "heading" => 18, "subheading" => 16, "caption" => 12, _ => 14 },
            FontWeight = variant is "title" or "heading" or "subheading" ? FontWeight.SemiBold : FontWeight.Normal
        };
        var color = context.GetString("color");
        if (color.Length > 0 && Color.TryParse(color, out var parsed)) block.Foreground = new SolidColorBrush(parsed);
        return block;
    }

    private static Control Icon(JsonRenderComponentContext context) => new TextBlock
    {
        Text = context.GetString("name", context.GetString("label", "•")),
        FontSize = context.GetNumber("size", 16),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Control Badge(JsonRenderComponentContext context) => new Border
    {
        Padding = new Thickness(8, 3),
        CornerRadius = new CornerRadius(10),
        Background = new SolidColorBrush(Color.FromArgb(36, 0, 120, 215)),
        Child = new TextBlock { Text = context.GetString("label", context.GetString("text")) }
    };

    private static Control Avatar(JsonRenderComponentContext context)
    {
        var name = context.GetString("alt", context.GetString("name", "?"));
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0])).ToUpperInvariant();
        return new Border
        {
            Width = context.GetNumber("size", 40),
            Height = context.GetNumber("size", 40),
            CornerRadius = new CornerRadius(100),
            Background = new SolidColorBrush(Color.FromArgb(44, 0, 120, 215)),
            Child = new TextBlock { Text = initials, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    private static Control Metric(JsonRenderComponentContext context)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock { Text = context.GetString("label"), FontSize = 12, Opacity = 0.75 });
        panel.Children.Add(new TextBlock { Text = context.GetString("value"), FontSize = context.GetNumber("valueSize", 24), FontWeight = FontWeight.SemiBold });
        var change = context.GetString("change");
        if (change.Length > 0) panel.Children.Add(new TextBlock { Text = change, FontSize = 12 });
        return panel;
    }

    private static void RegisterMany(JsonRenderComponentRegistry registry, IEnumerable<string> names, JsonRenderComponentFactory factory)
    {
        foreach (var name in names) registry.Register(name, factory);
    }
}
