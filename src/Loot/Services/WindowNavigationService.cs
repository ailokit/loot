using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Loot.ViewModels;
using Loot.Views;
using Loot.Plugins;
using Loot.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Loot.Services;

/// <summary>
/// Coordinates top-level windows. Each window is resolved from a dedicated
/// <see cref="IServiceScope"/> so that the window, its ViewModel, and all
/// transient dependencies are scoped to the window's lifetime. The scope is
/// disposed when the window closes, releasing all transient resources.
/// </summary>
/// <remarks>
/// ViewModel initialization and data loading are handled internally by each
/// window in <see cref="Window.OnOpened"/>. This service only creates windows
/// and passes pre-show parameters (conversation id, page key).
/// </remarks>
public sealed class WindowNavigationService(IServiceScopeFactory scopeFactory) : IWindowNavigationService
{
    private SettingsWindow? _settingsWindow;
    private readonly Dictionary<string, PluginToolWindow> _pluginWindows = new(StringComparer.OrdinalIgnoreCase);

    public void ShowSettings()
    {
        ShowSettings(SettingsWindowViewModel.GeneralSettingsPage);
    }

    public void ShowSkillSettings()
    {
        ShowSettings(SettingsWindowViewModel.SkillsSettingsPage);
    }

    public void ShowPluginTool(string pluginId, string toolId)
    {
        var key = $"{pluginId}/{toolId}";
        IServiceScope? scope = null;
        PluginToolWindow? window = null;
        try
        {
            if (_pluginWindows.TryGetValue(key, out var existing) && existing.IsVisible)
            {
                existing.Activate();
                existing.Focus();
                return;
            }

            scope = scopeFactory.CreateScope();
            window = scope.ServiceProvider.GetRequiredService<PluginToolWindow>();
            window.SetScope(scope);
            window.PluginId = pluginId;
            window.ToolId = toolId;
            window.Closed += (_, _) =>
            {
                if (_pluginWindows.TryGetValue(key, out var current) && ReferenceEquals(current, window))
                    _pluginWindows.Remove(key);
            };
            _pluginWindows[key] = window;
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(WindowNavigationService), $"Failed to open plugin tool '{pluginId}/{toolId}'", LogLevel.Warning);
            _pluginWindows.Remove(key);
            if (window is not null)
            {
                try { window.Close(); }
                catch
                {
                    try { scope?.Dispose(); }
                    catch { }
                }
            }
            else
            {
                try { scope?.Dispose(); }
                catch { }
            }
        }
    }

    public void ClosePluginTools(string pluginId)
    {
        foreach (var window in _pluginWindows.Where(pair => pair.Key.StartsWith(pluginId + "/", StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value).ToArray())
        {
            try { window.Close(); }
            catch (Exception exception) { ExceptionLogger.Log(exception, nameof(WindowNavigationService), $"Failed to close plugin tool window for '{pluginId}'", LogLevel.Warning); }
        }
    }

    private void ShowSettings(string pageKey)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.NavigateTo(pageKey);
            _settingsWindow.Activate();
            _settingsWindow.Focus();
            return;
        }

        var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<SettingsWindow>();
        window.SetScope(scope);
        window.PendingPageKey = pageKey;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, _settingsWindow))
                _settingsWindow = null;
        };

        _settingsWindow = window;
        window.Show();
        window.Activate();
        window.Focus();
    }
}
