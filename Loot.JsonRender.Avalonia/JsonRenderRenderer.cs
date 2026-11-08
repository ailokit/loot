using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace Loot.JsonRender.Avalonia;

/// <summary>Renders the supported json-render root/elements spec subset to native Avalonia controls.</summary>
public sealed partial class JsonRenderRenderer
{
    public const int MaximumSpecBytes = 1024 * 1024;
    public const int MaximumElements = 500;
    public const int MaximumDepth = 32;
    private const int MaximumRenderedControls = 5000;
    private const int MaximumRepeatItems = 500;

    private readonly IReadOnlyDictionary<string, JsonElement> _elements;
    private readonly JsonObject _state;
    private readonly Action<string, JsonObject> _actionSink;
    private readonly Action? _stateChanged;
    private readonly Action<Exception>? _errorSink;
    private readonly IReadOnlyDictionary<string, Func<JsonObject, JsonNode?>> _computedFunctions;
    private readonly JsonRenderComponentRegistry _components;
    private readonly List<WatcherBinding> _watchers = [];
    private int _renderedControls;

    private JsonRenderRenderer(
        IReadOnlyDictionary<string, JsonElement> elements,
        JsonObject state,
        Action<string, JsonObject> actionSink,
        Action? stateChanged,
        Action<Exception>? errorSink,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?>>? computedFunctions,
        JsonRenderComponentRegistry? components)
    {
        _elements = elements;
        _state = state;
        _actionSink = actionSink;
        _stateChanged = stateChanged;
        _errorSink = errorSink;
        _computedFunctions = computedFunctions ?? new Dictionary<string, Func<JsonObject, JsonNode?>>(StringComparer.Ordinal);
        _components = components ?? JsonRenderComponentRegistry.CreateDefault();
    }

    /// <summary>
    /// Renders a json-render flat spec. A caller-owned state object is mutated by two-way bindings;
    /// pass <paramref name="stateChanged"/> to refresh the view after a state edit.
    /// </summary>
    public static Control Render(
        string json,
        JsonObject? state = null,
        Action<string, JsonObject>? actionSink = null,
        Action? stateChanged = null,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?>>? computedFunctions = null,
        Action<Exception>? errorSink = null,
        JsonRenderComponentRegistry? components = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumSpecBytes)
            throw new JsonRenderException($"UI spec exceeds {MaximumSpecBytes} bytes.");

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumDepth });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("root", out var rootIdElement) || rootIdElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("elements", out var elementMap) || elementMap.ValueKind != JsonValueKind.Object)
            throw new JsonRenderException("Spec must contain a string 'root' and an 'elements' object.");

        var sourceElements = elementMap.EnumerateObject().ToArray();
        if (sourceElements.Length is 0 or > MaximumElements)
            throw new JsonRenderException($"Spec must contain between 1 and {MaximumElements} elements.");

        var elements = sourceElements.ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
        var rendererState = state ?? ReadInitialState(root);
        if (state is not null && root.TryGetProperty("state", out var initialState) && initialState.ValueKind == JsonValueKind.Object)
            MergeMissing(rendererState, initialState);

        var renderer = new JsonRenderRenderer(elements, rendererState, actionSink ?? ((_, _) => { }), stateChanged, errorSink, computedFunctions, components);
        var rootId = rootIdElement.GetString()!;
        if (!elements.ContainsKey(rootId))
            throw new JsonRenderException($"Root element '{rootId}' does not exist.");
        return renderer.Build(rootId, new HashSet<string>(StringComparer.Ordinal), new JsonRenderScope(), 0);
    }

    private static JsonObject ReadInitialState(JsonElement root) =>
        root.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object
            ? (JsonNode.Parse(state.GetRawText()) as JsonObject ?? new JsonObject())
            : new JsonObject();

    private static void MergeMissing(JsonObject target, JsonElement source)
    {
        foreach (var property in source.EnumerateObject())
            if (!target.ContainsKey(property.Name))
                target[property.Name] = JsonNode.Parse(property.Value.GetRawText());
    }

    private Control Build(string id, HashSet<string> path, JsonRenderScope scope, int depth)
    {
        if (++_renderedControls > MaximumRenderedControls)
            throw new JsonRenderException($"Rendered UI exceeds {MaximumRenderedControls} controls.");
        if (depth > MaximumDepth)
            throw new JsonRenderException("UI element tree is too deep.");
        if (!path.Add(id))
            throw new JsonRenderException($"UI element cycle detected at '{id}'.");
        if (!_elements.TryGetValue(id, out var element))
            throw new JsonRenderException($"Child element '{id}' does not exist.");
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            throw new JsonRenderException($"Element '{id}' must have a string type.");

        var type = typeElement.GetString()!;
        var props = element.TryGetProperty("props", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var children = GetChildren(element, id);
        var slots = GetSlots(element, id);
        RegisterWatchers(element, scope);
        Control control = HasRepeat(element)
            ? BuildRepeatedElement(id, type, props, children, slots, element.GetProperty("repeat"), path, scope, depth)
            : BuildComponent(id, type, props, children, slots, path, scope, depth);

        if (element.TryGetProperty("visible", out var visible))
            control.IsVisible = EvaluateVisibility(visible, scope);
        path.Remove(id);
        return control;
    }

    private Control BuildRepeatedElement(string id, string type, JsonElement props, IReadOnlyList<string> children,
        IReadOnlyDictionary<string, IReadOnlyList<string>> slots,
        JsonElement repeat, HashSet<string> path, JsonRenderScope outerScope, int depth)
    {
        if (repeat.ValueKind != JsonValueKind.Object || !repeat.TryGetProperty("statePath", out var statePathElement) ||
            statePathElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Object))
            throw new JsonRenderException($"Element '{id}' repeat requires a JSON Pointer or $item statePath.");
        var (array, itemPath) = ResolveRepeatSource(statePathElement, outerScope);
        if (array is null) return BuildComponent(id, type, props, [], slots, path, outerScope, depth);
        if (array.Count > MaximumRepeatItems)
            throw new JsonRenderException($"Element '{id}' repeat exceeds {MaximumRepeatItems} items.");

        var repeatedChildren = new List<Control>(array.Count * children.Count);
        for (var index = 0; index < array.Count; index++)
        {
            var item = array[index];
            var indexedPath = itemPath.Append(index.ToString(CultureInfo.InvariantCulture)).ToArray();
            var scope = new JsonRenderScope(item?.DeepClone(), index, indexedPath);
            foreach (var child in children)
                repeatedChildren.Add(Build(child, path, scope, depth + 1));
        }
        return BuildComponent(id, type, props, [], slots, path, outerScope, depth, repeatedChildren);
    }

    private Control BuildComponent(string id, string type, JsonElement props, IReadOnlyList<string> children,
        IReadOnlyDictionary<string, IReadOnlyList<string>> slots, HashSet<string> path, JsonRenderScope scope, int depth,
        IReadOnlyList<Control>? prebuiltChildren = null)
    {
        var renderedChildren = prebuiltChildren ?? children.Select(child => Build(child, path, scope, depth + 1)).ToArray();
        var renderedSlots = slots.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<Control>)pair.Value.Select(child => Build(child, path, scope, depth + 1)).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var context = new JsonRenderComponentContext(this, scope, id, props, renderedChildren, renderedSlots);
        if (!_components.TryRender(type, context, out var control) || control is null)
            throw new JsonRenderException($"Unknown component type '{type}' at element '{id}'.");
        return control;
    }

    private static IReadOnlyList<string> GetChildren(JsonElement element, string id)
    {
        if (!element.TryGetProperty("children", out var children)) return [];
        if (children.ValueKind != JsonValueKind.Array || children.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new JsonRenderException($"Element '{id}' children must be an array of IDs.");
        return children.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> GetSlots(JsonElement element, string id)
    {
        if (!element.TryGetProperty("slots", out var slots)) return new Dictionary<string, IReadOnlyList<string>>();
        if (slots.ValueKind != JsonValueKind.Object) throw new JsonRenderException($"Element '{id}' slots must be an object.");
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in slots.EnumerateObject())
        {
            if (slot.Value.ValueKind != JsonValueKind.Array || slot.Value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                throw new JsonRenderException($"Element '{id}' slot '{slot.Name}' must be an array of element IDs.");
            result[slot.Name] = slot.Value.EnumerateArray().Select(item => item.GetString()!).ToArray();
        }
        return result;
    }

    // Repeat content is prebuilt before the component's slots are used. The element's slots are
    // intentionally read in Build and are passed through this per-render cache.
    private bool HasRepeat(JsonElement element) => element.TryGetProperty("repeat", out var repeat) && repeat.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private sealed record WatcherBinding(string Path, string Action, JsonElement Parameters, JsonRenderScope Scope);
}
