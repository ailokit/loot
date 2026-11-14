using Microsoft.Extensions.DependencyInjection;

namespace Loot.AI.Skills;

public static class LootAiSkillsExtensions
{
    public static IServiceCollection AddLootSkills(this IServiceCollection services)
    {
        services.AddScoped<SkillRepository>()
            .AddScoped<SkillService>()
            .AddSingleton<AgentSkillsService>();

        return services;
    }
}
