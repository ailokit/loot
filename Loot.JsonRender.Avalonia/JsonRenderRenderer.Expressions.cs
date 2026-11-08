using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace Loot.JsonRender.Avalonia;

public sealed partial class JsonRenderRenderer
{
    private JsonNode? Resolve(JsonElement value, JsonRenderScope scope)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var array = new JsonArray();
            foreach (var item in value.EnumerateArray()) array.Add(Resolve(item, scope));
            return array;
        }
        if (value.ValueKind != JsonValueKind.Object) return JsonNode.Parse(value.GetRawText());
        if (value.TryGetProperty("$state", out var stateRef) && stateRef.ValueKind == JsonValueKind.String)
            return FindState(stateRef.GetString()!)?.DeepClone();
        if (value.TryGetProperty("$item", out var itemRef))
        {
            if (scope.Item is null) return null;
            var key = itemRef.ValueKind == JsonValueKind.String ? itemRef.GetString()! : "";
            return key.Length == 0 ? scope.Item.DeepClone() : (scope.Item is JsonObject item ? item[key]?.DeepClone() : null);
        }
        if (value.TryGetProperty("$index", out _)) return scope.Index is { } index ? JsonValue.Create(index) : null;
        if (value.TryGetProperty("$template", out var template) && template.ValueKind == JsonValueKind.String)
            return JsonValue.Create(ResolveTemplate(template.GetString()!, scope));
        if (value.TryGetProperty("$cond", out var condition) && value.TryGetProperty("$then", out var thenValue) && value.TryGetProperty("$else", out var elseValue))
            return Resolve(EvaluateVisibility(condition, scope) ? thenValue : elseValue, scope);
        if (value.TryGetProperty("$computed", out var computedName) && computedName.ValueKind == JsonValueKind.String)
        {
            var args = value.TryGetProperty("args", out var argsElement) && Resolve(argsElement, scope) is JsonObject obj ? obj : new JsonObject();
            return _computedFunctions.TryGetValue(computedName.GetString()!, out var function) ? function(args)?.DeepClone() : BuiltInComputed(computedName.GetString()!, args);
        }
        var resolved = new JsonObject();
        foreach (var property in value.EnumerateObject()) resolved[property.Name] = Resolve(property.Value, scope);
        return resolved;
    }

    private JsonNode? BuiltInComputed(string name, JsonObject args)
    {
        string Arg(string key) => args[key]?.ToString() ?? "";
        return name switch
        {
            "concat" => JsonValue.Create(string.Concat(args.Select(pair => pair.Value?.ToString() ?? ""))),
            "sum" => JsonValue.Create(args.Sum(pair => double.TryParse(pair.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0)),
            "uppercase" => JsonValue.Create(Arg("value").ToUpperInvariant()),
            "lowercase" => JsonValue.Create(Arg("value").ToLowerInvariant()),
            "join" => JsonValue.Create(args["values"] is JsonArray array ? string.Join(Arg("separator"), array.Select(item => item?.ToString() ?? "")) : ""),
            _ => throw new JsonRenderException($"Computed function '{name}' is not registered.")
        };
    }

    private string ResolveTemplate(string template, JsonRenderScope scope)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < template.Length;)
        {
            if (template[i] == '$' && i + 1 < template.Length && template[i + 1] == '{')
            {
                var end = template.IndexOf('}', i + 2);
                if (end >= 0)
                {
                    result.Append(FindState(template[(i + 2)..end])?.ToString() ?? "");
                    i = end + 1;
                    continue;
                }
            }
            result.Append(template[i++]);
        }
        return result.ToString();
    }

    private bool EvaluateVisibility(JsonElement condition, JsonRenderScope scope)
    {
        if (condition.ValueKind == JsonValueKind.Array) return condition.EnumerateArray().All(item => EvaluateVisibility(item, scope));
        if (condition.ValueKind == JsonValueKind.True) return true;
        if (condition.ValueKind is JsonValueKind.False or JsonValueKind.Null) return false;
        if (condition.ValueKind != JsonValueKind.Object) return IsTruthy(JsonNode.Parse(condition.GetRawText()));
        if (condition.TryGetProperty("$or", out var alternatives) && alternatives.ValueKind == JsonValueKind.Array)
            return alternatives.EnumerateArray().Any(item => EvaluateVisibility(item, scope));
        if (!condition.TryGetProperty("$state", out var stateRef) || stateRef.ValueKind != JsonValueKind.String)
            return true;
        var actual = FindState(stateRef.GetString()!);
        var result = IsTruthy(actual);
        foreach (var op in new[] { "eq", "neq", "gt", "gte", "lt", "lte" })
        {
            if (!condition.TryGetProperty(op, out var expectedElement)) continue;
            var expected = Resolve(expectedElement, scope);
            var comparison = Compare(actual, expected);
            result = op switch
            {
                "eq" => comparison == 0,
                "neq" => comparison != 0,
                "gt" => comparison > 0,
                "gte" => comparison >= 0,
                "lt" => comparison < 0,
                "lte" => comparison <= 0,
                _ => result
            };
            break;
        }
        return condition.TryGetProperty("not", out var not) && not.ValueKind == JsonValueKind.True ? !result : result;
    }

    private static int Compare(JsonNode? left, JsonNode? right)
    {
        if (double.TryParse(left?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var leftNumber) &&
            double.TryParse(right?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);
        return string.Compare(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private (JsonArray? Array, string[] BasePath) ResolveRepeatSource(JsonElement statePath, JsonRenderScope scope)
    {
        if (statePath.ValueKind == JsonValueKind.String)
        {
            var path = statePath.GetString()!;
            return (FindState(path) as JsonArray, JsonPointer(path));
        }
        if (statePath.TryGetProperty("$item", out var itemField) && scope.ItemPath is not null)
        {
            var field = itemField.ValueKind == JsonValueKind.String ? itemField.GetString() : "";
            var itemPath = field is null or "" ? scope.ItemPath : scope.ItemPath.Append(field).ToArray();
            var source = field is null or "" ? scope.Item : scope.Item is JsonObject item ? item[field] : null;
            return (source as JsonArray, itemPath);
        }
        return (null, []);
    }

    private JsonNode? FindState(string pointer)
    {
        var segments = JsonPointer(pointer);
        JsonNode? current = _state;
        foreach (var segment in segments)
        {
            current = current switch
            {
                JsonObject obj => obj[segment],
                JsonArray array when int.TryParse(segment, out var index) && index >= 0 && index < array.Count => array[index],
                _ => null
            };
        }
        return current;
    }

    private static string[] JsonPointer(string pointer)
    {
        if (pointer.Length == 0) return [];
        if (!pointer.StartsWith('/')) throw new JsonRenderException("State path must be a JSON Pointer.");
        return pointer[1..].Split('/').Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
    }

    private string GetStateString(string pointer) => FindState(pointer)?.ToString() ?? "";
    private bool GetStateBool(string pointer) => bool.TryParse(FindState(pointer)?.ToString(), out var value) && value;
    private double GetStateNumber(string pointer, double fallback) => double.TryParse(FindState(pointer)?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private string? GetBindingPath(JsonElement props, string property, JsonRenderScope scope)
    {
        if (!TryGetProperty(props, property, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        if (value.TryGetProperty("$bindState", out var bind) && bind.ValueKind == JsonValueKind.String) return bind.GetString();
        if (value.TryGetProperty("$bindItem", out var itemField) && scope.ItemPath is not null)
        {
            var field = itemField.ValueKind == JsonValueKind.String ? itemField.GetString() : "";
            return field is null || field.Length == 0 ? "/" + string.Join('/', scope.ItemPath) : "/" + string.Join('/', scope.ItemPath.Append(field));
        }
        return null;
    }

    private string? GetLegacyStatePath(JsonElement props, JsonRenderScope scope)
    {
        if (!TryGetProperty(props, "statePath", out var value) || value.ValueKind != JsonValueKind.String) return null;
        var path = value.GetString();
        return path is null ? null : scope.ItemPath is null ? path : "/" + string.Join('/', scope.ItemPath);
    }

    private string GetString(JsonElement props, string name, string fallback, JsonRenderScope scope) => ResolveString(props, name, scope, fallback);
    private string ResolveString(JsonElement props, string name, JsonRenderScope scope, string fallback) =>
        TryGetProperty(props, name, out var value) ? AsString(Resolve(value, scope)) ?? fallback : fallback;

    private bool GetBool(JsonElement props, string name, bool fallback, JsonRenderScope scope) =>
        TryGetProperty(props, name, out var value) ? AsBool(Resolve(value, scope), fallback) : fallback;

    private double GetDouble(JsonElement props, string name, double fallback, JsonRenderScope scope) =>
        TryGetProperty(props, name, out var value) ? AsDouble(Resolve(value, scope), fallback) : fallback;

    private Thickness GetPadding(JsonElement props, string name, Thickness fallback, JsonRenderScope scope)
    {
        if (!TryGetProperty(props, name, out var value)) return fallback;
        var resolved = Resolve(value, scope);
        if (resolved is JsonObject padding)
            return new Thickness(ToDouble(padding["left"], 0), ToDouble(padding["top"], 0), ToDouble(padding["right"], 0), ToDouble(padding["bottom"], 0));
        if (resolved is JsonValue token && token.TryGetValue<string>(out var size))
            return new Thickness(size.ToLowerInvariant() switch { "none" or "0" => 0, "xs" => 4, "sm" => 8, "md" => 12, "lg" => 20, "xl" => 28, _ => fallback.Left });
        var amount = AsDouble(resolved, fallback.Left);
        return new Thickness(Math.Clamp(amount, 0, 100));
    }

    private double GetSpacing(JsonElement props, JsonRenderScope scope)
    {
        var name = TryGetProperty(props, "gap", out _) ? "gap" : "spacing";
        if (!TryGetProperty(props, name, out var value)) return 8;
        var resolved = Resolve(value, scope);
        if (resolved is JsonValue token && token.TryGetValue<string>(out var size))
            return size.ToLowerInvariant() switch { "none" or "0" => 0, "xs" => 4, "sm" => 8, "md" => 12, "lg" => 20, "xl" => 28, _ => 8 };
        return Math.Clamp(AsDouble(resolved, 8), 0, 100);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        }
        value = default;
        return false;
    }

    private static string? AsString(JsonNode? node) => node is null ? null : node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();
    private static bool AsBool(JsonNode? node, bool fallback) => node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : bool.TryParse(node?.ToString(), out result) ? result : fallback;
    private static double AsDouble(JsonNode? node, double fallback) => double.TryParse(node?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? Math.Clamp(result, -10000, 10000) : fallback;
    private static double ToDouble(JsonNode? node, double fallback) => AsDouble(node, fallback);
    private static bool IsTruthy(JsonNode? node) => node switch { null => false, JsonValue value when value.TryGetValue<bool>(out var boolean) => boolean, JsonValue value when value.TryGetValue<double>(out var number) => number != 0, JsonValue value when value.TryGetValue<string>(out var text) => text.Length > 0, _ => true };
}
