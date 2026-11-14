using Microsoft.Extensions.DependencyInjection;

namespace Loot.AI.Conversations;

public static class LootAiConversationExtensions
{
    public static IServiceCollection AddLootAiConversations(this IServiceCollection services)
    {
        services.AddScoped<ConversationRepository>()
            .AddScoped<ConversationService>()
            .AddSingleton<SessionRunLock>();
        return services;
    }
}