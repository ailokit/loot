using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Loot.JsonRender.Avalonia;

/// <summary>Registers status, progress and feedback components.</summary>
internal static class FeedbackComponents
{
    public static void Register(JsonRenderComponentRegistry registry)
    {
        RegisterMany(registry, ["Progress", "ProgressBar"], Progress);
        registry.Register("Alert", Alert);
    }

    private static Control Progress(JsonRenderComponentContext context)
    {
        var minimum = context.GetNumber("min", 0);
        var maximum = Math.Max(minimum, context.GetNumber("max", 100));
        return new ProgressBar
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(context.GetNumber("value", minimum), minimum, maximum),
            IsIndeterminate = context.GetBool("isIndeterminate")
        };
    }

    private static Control Alert(JsonRenderComponentContext context)
    {
        var panel = new StackPanel { Spacing = 4 };
        var title = context.GetString("title");
        var message = context.GetString("message", context.GetString("text"));
        if (title.Length > 0) panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        if (message.Length > 0) panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        foreach (var child in context.Children) panel.Children.Add(child);
        foreach (var child in context.Slots.Values.SelectMany(slot => slot)) panel.Children.Add(child);

        var background = context.GetString("variant", "info") switch
        {
            "success" => Color.FromArgb(38, 38, 150, 80),
            "warning" => Color.FromArgb(42, 210, 150, 0),
            "error" or "danger" => Color.FromArgb(42, 220, 60, 60),
            _ => Color.FromArgb(32, 0, 120, 215)
        };
        return new Border
        {
            Padding = context.GetPadding("padding", new Thickness(10)),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(background),
            Child = panel
        };
    }

    private static void RegisterMany(JsonRenderComponentRegistry registry, IEnumerable<string> names, JsonRenderComponentFactory factory)
    {
        foreach (var name in names) registry.Register(name, factory);
    }
}
