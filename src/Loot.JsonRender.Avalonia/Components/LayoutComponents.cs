using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Loot.JsonRender.Avalonia;

/// <summary>Registers the host's layout and container strategies.</summary>
internal static class LayoutComponents
{
    public static void Register(JsonRenderComponentRegistry registry)
    {
        RegisterMany(registry, ["Column", "Stack", "VStack"], context => Panel(context, Orientation.Vertical));
        RegisterMany(registry, ["Row", "HStack"], context => Panel(context, Orientation.Horizontal));
        registry.Register("Card", Card);
        RegisterMany(registry, ["CardHeader", "CardFooter"], context => Panel(context, Orientation.Vertical, IncludeSlots(context)));
        RegisterMany(registry, ["Group", "Panel"], context =>
            new Border { Padding = new Thickness(8), Child = Panel(context, Orientation.Vertical, IncludeSlots(context)) });
        RegisterMany(registry, ["ScrollViewer", "ScrollView"], context =>
            new ScrollViewer
            {
                Content = Panel(context, Orientation.Vertical, IncludeSlots(context)),
                MaxHeight = context.GetNumber("maxHeight", double.PositiveInfinity),
                MaxWidth = context.GetNumber("maxWidth", double.PositiveInfinity)
            });
        RegisterMany(registry, ["Divider", "Separator"], _ =>
            new Border { Height = 1, Background = Brushes.Gray, Margin = new Thickness(0, 4) });
    }

    private static Control Card(JsonRenderComponentContext context)
    {
        var content = new StackPanel { Spacing = 8 };
        var title = context.GetString("title");
        var description = context.GetString("description");
        if (title.Length > 0) content.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold });
        if (description.Length > 0) content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        AddSlot(content, context, "header");
        foreach (var child in context.Children) content.Children.Add(child);
        AddSlot(content, context, "footer");
        return new Border
        {
            Padding = context.GetPadding("padding", new Thickness(12)),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128)),
            Child = content
        };
    }

    private static StackPanel Panel(JsonRenderComponentContext context, Orientation orientation, IEnumerable<Control>? additional = null)
    {
        var panel = new StackPanel
        {
            Orientation = orientation,
            Spacing = context.GetSpacing(),
            VerticalAlignment = orientation == Orientation.Horizontal ? VerticalAlignment.Center : VerticalAlignment.Stretch
        };
        foreach (var child in context.Children) panel.Children.Add(child);
        if (additional is not null) foreach (var child in additional) panel.Children.Add(child);
        return panel;
    }

    private static IEnumerable<Control> IncludeSlots(JsonRenderComponentContext context) =>
        context.Slots.Values.SelectMany(slot => slot);

    private static void AddSlot(Panel panel, JsonRenderComponentContext context, string name)
    {
        if (!context.Slots.TryGetValue(name, out var controls)) return;
        foreach (var control in controls) panel.Children.Add(control);
    }

    private static void RegisterMany(JsonRenderComponentRegistry registry, IEnumerable<string> names, JsonRenderComponentFactory factory)
    {
        foreach (var name in names) registry.Register(name, factory);
    }
}
