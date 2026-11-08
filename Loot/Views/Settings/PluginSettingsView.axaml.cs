using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Loot.ViewModels;
using Loot.Logging;

namespace Loot.Views.Settings;

public partial class PluginSettingsView : UserControl
{
    public PluginSettingsView() => InitializeComponent();

    private async void OnInstallClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PluginSettingsViewModel viewModel) return;
        try
        {
            if (TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider }) return;
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "选择插件文件夹" });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) await viewModel.InstallAsync(path);
        }
        catch (Exception exception)
        {
            viewModel.StatusMessage = exception.Message;
            ExceptionLogger.Log(exception, nameof(PluginSettingsView), "Failed to install plugin from directory");
        }
    }

    private async void OnInstallArchiveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PluginSettingsViewModel viewModel) return;
        try
        {
            if (TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider }) return;
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "选择 Loot 插件包",
                FileTypeFilter = [new FilePickerFileType("Loot 插件包") { Patterns = ["*.lootplugin", "*.zip"] }]
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) await viewModel.InstallArchiveAsync(path);
        }
        catch (Exception exception)
        {
            viewModel.StatusMessage = exception.Message;
            ExceptionLogger.Log(exception, nameof(PluginSettingsView), "Failed to install plugin archive");
        }
    }
}
