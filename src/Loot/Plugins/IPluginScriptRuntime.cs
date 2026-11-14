using System.Text.Json.Nodes;

namespace Loot.Plugins;

public interface IPluginScriptRuntime : IAsyncDisposable
{
    event EventHandler<PluginRuntimeMessageEventArgs>? Message;
    Task StartAsync(string script, string fileName, CancellationToken cancellationToken = default);
    Task DispatchActionAsync(string action, JsonObject payload, CancellationToken cancellationToken = default);
}

public sealed class PluginRuntimeMessageEventArgs(string name, JsonNode? payload) : EventArgs
{
    public string Name { get; } = name;
    public JsonNode? Payload { get; } = payload;
}

public interface IPluginScriptRuntimeFactory
{
    IPluginScriptRuntime Create();
}
