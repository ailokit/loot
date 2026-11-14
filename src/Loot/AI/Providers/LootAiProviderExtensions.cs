using Microsoft.Extensions.DependencyInjection;

namespace Loot.AI.Providers;

public static class LootAiProviderExtensions
{
    public static IServiceCollection AddLootAiProviders(this IServiceCollection services)
    {
        services.AddScoped<ApiProviderRepository>();
        services.AddScoped<IProviderConnectionTester, ProviderConnectionTester>();
        services.AddScoped<ProviderService>();
        
        return services;
    }
}