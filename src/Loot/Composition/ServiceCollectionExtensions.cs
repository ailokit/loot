using Loot.AI;
using Loot.AI.Mcp;
using Loot.AI.Tools;
using Loot.Data;
using Loot.Jobs;
using Loot.Localization;
using Loot.Logging;
using Loot.Plugins;
using Loot.Services;
using Loot.ViewModels;
using Loot.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AppState = Loot.ViewModels.AppState;

namespace Loot.Composition;

/// <summary>Registers the application's composition root and performs required local-database startup work.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers all production services. An optional path override supports isolated tests.</summary>
    public static IServiceCollection AddLootApplication(this IServiceCollection services, AppPaths? paths = null)
    {
        var appPaths = paths ?? AppPaths.CreateDefault();
        services.AddSingleton(appPaths);
        services.AddSingleton(DataDirectoryConfiguration.CreateDefault());
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<ILoggerProvider>(_ => new DailyFileLoggerProvider(appPaths.LogsDirectory));
        services.AddSingleton(static provider =>
        {
            var appPaths = provider.GetRequiredService<AppPaths>();
            appPaths.EnsureCreated();
            return new SqliteDatabase(appPaths.DatabasePath);
        });
        services.AddTransient<LazyServiceProvider>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<DataDirectoryService>();
        services.AddSingleton<PluginCatalog>();
        services.AddSingleton<IPluginScriptRuntimeFactory, QuickJsPluginScriptRuntimeFactory>();
        services.AddSingleton<MessageRepository>();
        services.AddSingleton<AppSettingRepository>();
        services.AddSingleton<IPlatformNotificationService, PlatformNotificationService>();
        services.AddSingleton<ITopmostNotificationService, TopmostNotificationService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISystemBrowserService, SystemBrowserService>();
        services.AddSingleton<CronJobRepository>();
        services.AddSingleton<CronJobScheduler>();
        services.AddSingleton<ICronJobHandler, NotificationJob>();
        services.AddSingleton<ICronJobHandler, AgentJob>();
        services.AddSingleton<ShellToolConfiguration>();
        services.AddSingleton<StartupRecoveryService>();
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<AppState>();
        services.AddSingleton<IUpdateService>(provider => new UpdateService(provider.GetRequiredService<AppPaths>(), provider.GetRequiredService<AppState>()));
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();
        services.AddSingleton<IWindowNavigationService, WindowNavigationService>();
        services.AddSingleton<IGlobalHotKeyService, GlobalHotKeyService>();
        services.AddScoped<ChatWindowViewModel>();
        services.AddScoped<SettingsWindowViewModel>();
        services.AddScoped<ChatWindow>();
        services.AddSingleton<ChatWindowManager>();
        services.AddScoped<SettingsWindow>();
        services.AddScoped<ApiKeySettingsViewModel>();
        services.AddScoped<GeneralSettingsViewModel>();
        services.AddScoped<ShortcutSettingsViewModel>();
        services.AddScoped<SkillSettingsViewModel>();
        services.AddScoped<AgentSkillsSettingsViewModel>();
        services.AddScoped<ToolSettingsViewModel>();
        services.AddScoped<McpSettingsViewModel>();
        services.AddScoped<HistorySettingsViewModel>();
        services.AddScoped<ScheduledJobsSettingsViewModel>();
        services.AddScoped<AboutSettingsViewModel>();
        services.AddScoped<PluginSettingsViewModel>();
        services.AddScoped<PluginToolWindow>();

        services.AddLootAi();
        return services;
    }

    /// <summary>
    /// Initializes the current schema and recovers interrupted messages before any window reads application data.
    /// </summary>
    public static void InitializeLootDatabase(this IServiceProvider services)
    {
        services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
        services.GetRequiredService<StartupRecoveryService>().RecoverAsync().GetAwaiter().GetResult();
    }
}
