using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace Loot.JsonRender.Avalonia;

public sealed partial class JsonRenderRenderer
{
    private void SetState(string pointer, JsonNode? value, bool notify = true)
    {
        var segments = JsonPointer(pointer);
        if (segments.Length == 0) throw new JsonRenderException("State pointer must address a property.");
        JsonNode? current = _state;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (current is JsonObject obj)
            {
                if (obj[segment] is not JsonObject and not JsonArray) obj[segment] = new JsonObject();
                current = obj[segment];
            }
            else if (current is JsonArray array && int.TryParse(segment, out var index) && index >= 0 && index < array.Count)
                current = array[index];
            else throw new JsonRenderException($"State pointer '{pointer}' does not address a writable object.");
        }
        var last = segments[^1];
        if (current is JsonObject target) target[last] = value;
        else if (current is JsonArray array && int.TryParse(last, out var arrayIndex) && arrayIndex >= 0 && arrayIndex < array.Count) array[arrayIndex] = value;
        else throw new JsonRenderException($"State pointer '{pointer}' does not address a writable property.");

        if (notify)
        {
            NotifyStateChanged();
            FireWatchers(pointer);
        }
    }

    internal void TrySetState(string pointer, JsonNode? value, bool notify = true)
    {
        try { SetState(pointer, value, notify); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void NotifyStateChanged()
    {
        try { _stateChanged?.Invoke(); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void TryFireWatchers(string path)
    {
        try { FireWatchers(path); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void SafeInvokeAction(string action, JsonObject payload)
    {
        try { _actionSink(action, payload); }
        catch (Exception exception) { ReportError(exception); }
    }

    internal JsonNode? ResolveProperty(JsonElement props, string name, JsonRenderScope scope) =>
        TryGetProperty(props, name, out var value) ? Resolve(value, scope) : null;

    internal string ReadString(JsonElement props, string name, string fallback, JsonRenderScope scope) =>
        GetString(props, name, fallback, scope);

    internal bool ReadBool(JsonElement props, string name, bool fallback, JsonRenderScope scope) =>
        GetBool(props, name, fallback, scope);

    internal double ReadNumber(JsonElement props, string name, double fallback, JsonRenderScope scope) =>
        GetDouble(props, name, fallback, scope);

    internal double ReadSpacing(JsonElement props, JsonRenderScope scope) => GetSpacing(props, scope);

    internal Thickness ReadPadding(JsonElement props, string name, Thickness fallback, JsonRenderScope scope) =>
        GetPadding(props, name, fallback, scope);

    internal bool TryGetBindingPath(JsonElement props, string property, JsonRenderScope scope, out string path)
    {
        var found = GetBindingPath(props, property, scope) ?? GetLegacyStatePath(props, scope);
        path = found ?? string.Empty;
        return found is not null;
    }

    internal string ReadStateString(string path) => GetStateString(path);
    internal bool ReadStateBoolean(string path) => GetStateBool(path);
    internal double ReadStateNumber(string path, double fallback) => GetStateNumber(path, fallback);

    internal void SetBoundValue(JsonElement props, string property, JsonNode? value, JsonRenderScope scope, bool notify)
    {
        if (TryGetBindingPath(props, property, scope, out var path)) TrySetState(path, value, notify);
    }

    internal void NotifyBoundValueChanged(JsonElement props, string property, JsonRenderScope scope)
    {
        if (!TryGetBindingPath(props, property, scope, out var path)) return;
        NotifyStateChanged();
        TryFireWatchers(path);
    }

    internal void EmitAction(string action, JsonObject? parameters, JsonRenderScope scope)
    {
        var payload = (JsonObject)_state.DeepClone();
        if (parameters is not null)
        {
            payload["params"] = parameters.DeepClone();
            foreach (var pair in parameters) payload[pair.Key] = pair.Value?.DeepClone();
        }
        SafeInvokeAction(action, payload);
    }

    private void ReportError(Exception exception)
    {
        try { _errorSink?.Invoke(exception); }
        catch { /* An error-reporting failure must never escape a plugin control event. */ }
    }

    private void RegisterWatchers(JsonElement element, JsonRenderScope scope)
    {
        if (!element.TryGetProperty("watch", out var watch) || watch.ValueKind != JsonValueKind.Object) return;
        foreach (var property in watch.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in property.Value.EnumerateArray()) AddWatcher(property.Name, action, scope);
            }
            else AddWatcher(property.Name, property.Value, scope);
        }
    }

    private void AddWatcher(string path, JsonElement binding, JsonRenderScope scope)
    {
        if (binding.ValueKind != JsonValueKind.Object || !binding.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String) return;
        var parameters = binding.TryGetProperty("params", out var args) ? args.Clone() : default;
        _watchers.Add(new WatcherBinding(path, action.GetString()!, parameters, scope));
    }

    private void FireWatchers(string changedPath)
    {
        foreach (var watcher in _watchers)
        {
            if (!string.Equals(watcher.Path, changedPath, StringComparison.Ordinal)) continue;
            var payload = (JsonObject)_state.DeepClone();
            if (watcher.Parameters.ValueKind == JsonValueKind.Object && Resolve(watcher.Parameters, watcher.Scope) is JsonObject parameters)
            {
                payload["params"] = parameters.DeepClone();
                foreach (var pair in parameters) payload[pair.Key] = pair.Value?.DeepClone();
            }
            SafeInvokeAction(watcher.Action, payload);
        }
    }

}
