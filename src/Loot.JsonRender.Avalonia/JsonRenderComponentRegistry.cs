using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace Loot.JsonRender.Avalonia;

/// <summary>Factory for a trusted, host-registered json-render component.</summary>
public delegate Control JsonRenderComponentFactory(JsonRenderComponentContext context);

/// <summary>
/// Maps json-render component types to native Avalonia factories. Plugins select registered names
/// in their JSON specs; the JSON itself cannot provide or load component code.
/// </summary>
public sealed class JsonRenderComponentRegistry
{
    private readonly Dictionary<string, JsonRenderComponentFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    public static JsonRenderComponentRegistry CreateDefault()
    {
        var registry = new JsonRenderComponentRegistry();
        LayoutComponents.Register(registry);
        ContentComponents.Register(registry);
        FormComponents.Register(registry);
        FeedbackComponents.Register(registry);
        return registry;
    }

    /// <summary>Registers or replaces a component factory. Register only host-owned code.</summary>
    public JsonRenderComponentRegistry Register(string type, JsonRenderComponentFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[type] = factory;
        return this;
    }

    public bool Remove(string type) => _factories.Remove(type);

    internal bool TryRender(string type, JsonRenderComponentContext context, out Control? control)
    {
        if (!_factories.TryGetValue(type, out var factory))
        {
            control = null;
            return false;
        }
        control = factory(context);
        return true;
    }
}

/// <summary>Resolved component inputs and safe host operations available to component factories.</summary>
public sealed class JsonRenderComponentContext
{
    private readonly JsonRenderRenderer _renderer;
    private readonly JsonRenderScope _scope;

    internal JsonRenderComponentContext(JsonRenderRenderer renderer, JsonRenderScope scope, string elementId,
        JsonElement props, IReadOnlyList<Control> children, IReadOnlyDictionary<string, IReadOnlyList<Control>> slots)
    {
        _renderer = renderer;
        _scope = scope;
        ElementId = elementId;
        Props = props;
        Children = children;
        Slots = slots;
    }

    public string ElementId { get; }
    public JsonElement Props { get; }
    public IReadOnlyList<Control> Children { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<Control>> Slots { get; }

    public JsonNode? GetValue(string name) => _renderer.ResolveProperty(Props, name, _scope);
    public string GetString(string name, string fallback = "") => _renderer.ReadString(Props, name, fallback, _scope);
    public bool GetBool(string name, bool fallback = false) => _renderer.ReadBool(Props, name, fallback, _scope);
    public double GetNumber(string name, double fallback = 0) => _renderer.ReadNumber(Props, name, fallback, _scope);
    public double GetSpacing() => _renderer.ReadSpacing(Props, _scope);
    public Thickness GetPadding(string name, Thickness fallback) => _renderer.ReadPadding(Props, name, fallback, _scope);
    public bool TryGetBindingPath(string property, out string path) => _renderer.TryGetBindingPath(Props, property, _scope, out path);
    public string GetBoundString(string path) => _renderer.ReadStateString(path);
    public bool GetBoundBoolean(string path) => _renderer.ReadStateBoolean(path);
    public double GetBoundNumber(string path, double fallback = 0) => _renderer.ReadStateNumber(path, fallback);
    public void SetBoundValue(string property, JsonNode? value, bool notify = true) => _renderer.SetBoundValue(Props, property, value, _scope, notify);
    public void NotifyBoundValueChanged(string property) => _renderer.NotifyBoundValueChanged(Props, property, _scope);
    public void SetState(string path, JsonNode? value) => _renderer.TrySetState(path, value);
    public void EmitAction(string action, JsonObject? parameters = null) => _renderer.EmitAction(action, parameters, _scope);
}

internal sealed record JsonRenderScope(JsonNode? Item = null, int? Index = null, string[]? ItemPath = null);
