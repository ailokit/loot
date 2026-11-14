using Loot.AI.Conversations;
using Loot.AI.Providers;
using Loot.AI.Mcp;
using Loot.AI.Skills;
using Loot.AI.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Loot.AI;

public static class LootAiExtensions
{
    public static IServiceCollection AddLootAi(this IServiceCollection services)
    {
        services.AddScoped<ChatService>()
            .AddSingleton<McpServerRepository>()
            .AddSingleton<McpClientService>()
            .AddLootTools()
            .AddLootAiConversations()
            .AddLootAiProviders()
            .AddLootSkills();
        
        return services;
    }
}
