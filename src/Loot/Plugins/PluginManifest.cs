using System.Text.Json.Serialization;

namespace Loot.Plugins;

public sealed class PluginManifest
{
    public int ManifestVersion { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Author { get; init; }
    public LootCompatibility? Loot { get; init; }
    public PluginRuntimeDefinition? Runtime { get; init; }
    public List<PluginToolDefinition> Tools { get; init; } = [];
    public List<string> Capabilities { get; init; } = [];
}

public sealed class LootCompatibility
{
    public string? MinVersion { get; init; }
    public string? MaxVersion { get; init; }
}

public sealed class PluginRuntimeDefinition
{
    public int ApiVersion { get; init; }
    public string? Entry { get; init; }
}

public sealed class PluginToolDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Ui { get; init; } = string.Empty;
    public string Entry { get; init; } = string.Empty;
    public string? Icon { get; init; }
}

public sealed record InstalledPlugin(string DirectoryPath, PluginManifest Manifest, bool IsEnabled, string? Error = null)
{
    public string Id => Manifest.Id;
}

internal sealed class PluginStateDocument
{
    public Dictionary<string, PluginStateEntry> Plugins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PluginStateEntry
{
    public bool Enabled { get; set; }
    public HashSet<string> GrantedCapabilities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
