namespace Loot.Plugins;

public sealed class QuickJsPluginScriptRuntimeFactory : IPluginScriptRuntimeFactory
{
    public IPluginScriptRuntime Create() => new QuickJsPluginScriptRuntime();
}
