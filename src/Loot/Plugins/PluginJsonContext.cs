using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loot.Plugins;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(PluginStateDocument))]
internal partial class PluginJsonContext : JsonSerializerContext;
