using System.Text.Json;
using System.Text.Json.Serialization;
using Loot.AI.Conversations;
using Loot.AI.Skills;
using Loot.AI.Tools;
using Loot.Services;
using Microsoft.Agents.AI;

namespace Loot.AI;

[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(UpdateService.GitHubRelease))]
[JsonSerializable(typeof(MessageAttachment[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(AgentSkillInstallMetadata))]
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]
[JsonSerializable(typeof(NotificationType))]
[JsonSerializable(typeof(SystemInformationType))]
internal sealed partial class LootJsonSerializerContext : JsonSerializerContext;

internal static class LootJsonSerializerOptions
{
    public static JsonSerializerOptions AgentSession { get; } = CreateAgentSessionOptions();

    private static JsonSerializerOptions CreateAgentSessionOptions()
    {
        var options = new JsonSerializerOptions(AgentAbstractionsJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(LootJsonSerializerContext.Default);
        options.MakeReadOnly();
        return options;
    }
}
