using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Loot.JsonRender.Avalonia;

/// <summary>Registers interactive inputs and routes edits through the renderer's state store.</summary>
internal static class FormComponents
{
    public static void Register(JsonRenderComponentRegistry registry)
    {
        RegisterMany(registry, ["Button", "IconButton"], Button);
        registry.Register("ToggleButton", ToggleButton);
        RegisterMany(registry, ["TextField", "TextInput", "TextBox", "Input"], context => TextInput(context, multiline: false));
        RegisterMany(registry, ["TextArea", "TextAreaField", "Textarea"], context => TextInput(context, multiline: true));
        RegisterMany(registry, ["NumberField", "NumberInput"], NumberInput);
        registry.Register("PasswordField", PasswordInput);
        RegisterMany(registry, ["CheckBox", "Checkbox"], CheckBox);
        RegisterMany(registry, ["Switch", "ToggleSwitch"], Switch);
        RegisterMany(registry, ["Select", "ComboBox"], Select);
        registry.Register("RadioGroup", RadioGroup);
        registry.Register("Slider", Slider);
        registry.Register("DatePicker", DatePicker);
    }

    private static Control Button(JsonRenderComponentContext context)
    {
        var button = new Button
        {
            Content = context.GetString("label", context.GetString("text", "Button")),
            HorizontalAlignment = ParseHorizontalAlignment(context.GetString("align", "Left")),
            IsEnabled = context.GetBool("enabled", true)
        };
        var action = context.GetString("action");
        if (action.Length > 0) button.Click += (_, _) => context.EmitAction(action, context.GetValue("params") as JsonObject);
        return button;
    }

    private static Control ToggleButton(JsonRenderComponentContext context)
    {
        var hasBinding = context.TryGetBindingPath("pressed", out _);
        var toggle = new ToggleButton
        {
            Content = context.GetString("label", "Toggle"),
            IsChecked = hasBinding ? context.GetBoundBoolean(GetPath(context, "pressed")) : context.GetBool("pressed", context.GetBool("value"))
        };
        if (hasBinding) toggle.IsCheckedChanged += (_, _) => context.SetBoundValue("pressed", JsonValue.Create(toggle.IsChecked ?? false));
        var action = context.GetString("action");
        if (action.Length > 0) toggle.Click += (_, _) => context.EmitAction(action, context.GetValue("params") as JsonObject);
        return toggle;
    }

    private static Control TextInput(JsonRenderComponentContext context, bool multiline)
    {
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var text = hasBinding ? context.GetBoundString(path) : context.GetString("value");
        var box = new TextBox
        {
            PlaceholderText = context.GetString("placeholder", context.GetString("label")),
            Text = text,
            MaxLength = 8192,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? context.GetNumber("minHeight", 90) : 0
        };
        if (hasBinding)
        {
            box.TextChanged += (_, _) => context.SetBoundValue("value", JsonValue.Create(box.Text ?? ""), notify: false);
            box.LostFocus += (_, _) => context.NotifyBoundValueChanged("value");
        }
        return box;
    }

    private static Control NumberInput(JsonRenderComponentContext context)
    {
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var box = new TextBox
        {
            PlaceholderText = context.GetString("placeholder", context.GetString("label")),
            Text = hasBinding ? context.GetBoundString(path) : context.GetString("value"),
            MaxLength = 80
        };
        if (hasBinding)
        {
            box.TextChanged += (_, _) =>
            {
                var value = box.Text ?? "";
                if (value.Length == 0) context.SetBoundValue("value", null, notify: false);
                else if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    context.SetBoundValue("value", JsonValue.Create(parsed), notify: false);
            };
            box.LostFocus += (_, _) => context.NotifyBoundValueChanged("value");
        }
        return box;
    }

    private static Control PasswordInput(JsonRenderComponentContext context)
    {
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var box = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = context.GetString("placeholder", context.GetString("label")),
            Text = hasBinding ? context.GetBoundString(path) : context.GetString("value"),
            MaxLength = 8192
        };
        if (hasBinding)
        {
            box.TextChanged += (_, _) => context.SetBoundValue("value", JsonValue.Create(box.Text ?? ""), notify: false);
            box.LostFocus += (_, _) => context.NotifyBoundValueChanged("value");
        }
        return box;
    }

    private static Control CheckBox(JsonRenderComponentContext context)
    {
        var hasBinding = context.TryGetBindingPath("checked", out var path);
        var checkBox = new CheckBox
        {
            Content = context.GetString("label", context.GetString("text")),
            IsChecked = hasBinding ? context.GetBoundBoolean(path) : context.GetBool("checked", context.GetBool("value")),
            IsThreeState = context.GetBool("isThreeState")
        };
        if (hasBinding) checkBox.IsCheckedChanged += (_, _) => context.SetBoundValue("checked", JsonValue.Create(checkBox.IsChecked ?? false));
        return checkBox;
    }

    private static Control Switch(JsonRenderComponentContext context)
    {
        var checkBox = (CheckBox)CheckBox(context);
        checkBox.Content = $"◉  {checkBox.Content}";
        return checkBox;
    }

    private static Control Select(JsonRenderComponentContext context)
    {
        var options = ParseOptions(context.GetValue("options"));
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var selected = hasBinding ? context.GetBoundString(path) : context.GetString("value");
        var items = options.Select(option => new ComboBoxItem { Content = option.Label, Tag = option.Value }).ToList();
        var combo = new ComboBox
        {
            ItemsSource = items,
            PlaceholderText = context.GetString("placeholder", context.GetString("label")),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = items.FirstOrDefault(option => string.Equals(option.Tag?.ToString(), selected, StringComparison.Ordinal))
        };
        if (hasBinding) combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: { } tag }) context.SetBoundValue("value", JsonValue.Create(tag.ToString()));
        };
        return combo;
    }

    private static Control RadioGroup(JsonRenderComponentContext context)
    {
        var options = ParseOptions(context.GetValue("options"));
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var selected = hasBinding ? context.GetBoundString(path) : context.GetString("value");
        var group = Guid.NewGuid().ToString("N");
        var panel = new StackPanel { Spacing = 4 };
        foreach (var option in options)
        {
            var radio = new RadioButton { Content = option.Label, GroupName = group, IsChecked = option.Value == selected };
            if (hasBinding) radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true) context.SetBoundValue("value", JsonValue.Create(option.Value));
            };
            panel.Children.Add(radio);
        }
        return panel;
    }

    private static Control Slider(JsonRenderComponentContext context)
    {
        var minimum = context.GetNumber("min");
        var maximum = Math.Max(minimum, context.GetNumber("max", 100));
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var value = hasBinding ? context.GetBoundNumber(path, minimum) : context.GetNumber("value", minimum);
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            TickFrequency = Math.Max(0.01, context.GetNumber("step", 1)),
            IsSnapToTickEnabled = context.GetBool("showTicks")
        };
        if (hasBinding) slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == global::Avalonia.Controls.Slider.ValueProperty) context.SetBoundValue("value", JsonValue.Create(slider.Value));
        };
        return slider;
    }

    private static Control DatePicker(JsonRenderComponentContext context)
    {
        var hasBinding = context.TryGetBindingPath("value", out var path);
        var dateText = hasBinding ? context.GetBoundString(path) : context.GetString("value");
        var picker = new DatePicker
        {
            SelectedDate = DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, out var date) ? date : null
        };
        if (hasBinding) picker.SelectedDateChanged += (_, _) => context.SetBoundValue("value", picker.SelectedDate is { } selected
            ? JsonValue.Create(selected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : null);
        return picker;
    }

    private static string GetPath(JsonRenderComponentContext context, string property)
    {
        context.TryGetBindingPath(property, out var path);
        return path;
    }

    private static IReadOnlyList<(string Label, string Value)> ParseOptions(JsonNode? node)
    {
        if (node is not JsonArray array) throw new JsonRenderException("Select requires an options array.");
        var options = new List<(string, string)>();
        foreach (var item in array)
        {
            if (item is JsonObject obj)
            {
                var label = obj["label"]?.ToString() ?? obj["name"]?.ToString() ?? obj["value"]?.ToString();
                var value = obj["value"]?.ToString();
                if (label is null || value is null) throw new JsonRenderException("Select options require label and value properties.");
                options.Add((label, value));
            }
            else if (item is JsonValue) options.Add((item.ToString(), item.ToString()));
            else throw new JsonRenderException("Select options must be strings or objects with label and value properties.");
        }
        return options;
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string value) => value.ToLowerInvariant() switch
    {
        "center" => HorizontalAlignment.Center,
        "right" or "end" => HorizontalAlignment.Right,
        "stretch" or "fill" => HorizontalAlignment.Stretch,
        _ => HorizontalAlignment.Left
    };

    private static void RegisterMany(JsonRenderComponentRegistry registry, IEnumerable<string> names, JsonRenderComponentFactory factory)
    {
        foreach (var name in names) registry.Register(name, factory);
    }
}
