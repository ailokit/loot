namespace Loot.Services;

public interface IWindowNavigationService
{
    void ShowSettings();
    void ShowSkillSettings();
    void ShowPluginTool(string pluginId, string toolId);
    void ClosePluginTools(string pluginId);
}
