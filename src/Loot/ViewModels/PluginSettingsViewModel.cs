using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loot.Localization;
using Loot.Plugins;
using Loot.Services;
using Loot.Logging;
using Microsoft.Extensions.Logging;

namespace Loot.ViewModels;

public sealed partial class PluginSettingsViewModel(PluginCatalog catalog, IWindowNavigationService navigation, IConfirmationService confirmation, LocalizationService localization) : SettingsViewModelBase(localization)
{
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    public Task InstallAsync(string directory, CancellationToken cancellationToken = default) =>
        InstallAndRefreshAsync(directory, cancellationToken);

    public async Task InstallArchiveAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await catalog.InstallFromArchiveAsync(archivePath, cancellationToken);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginSettingsViewModel), "Could not install plugin archive", LogLevel.Warning);
            StatusMessage = exception.Message;
        }
    }

    private async Task InstallAndRefreshAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            await catalog.InstallFromDirectoryAsync(directory, cancellationToken);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginSettingsViewModel), "Could not install plugin directory", LogLevel.Warning);
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var plugins = await catalog.ScanAsync();
            Plugins.Clear();
            foreach (var plugin in plugins)
                Plugins.Add(new PluginItemViewModel(plugin, catalog, navigation, confirmation, () => RefreshAsync(), message => StatusMessage = message));
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginSettingsViewModel), "Could not refresh plugin list", LogLevel.Warning);
            StatusMessage = exception.Message;
        }
        finally { IsLoading = false; }
    }
}

public sealed partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginCatalog _catalog;
    private readonly IWindowNavigationService _navigation;
    private readonly InstalledPlugin _plugin;

    private readonly IConfirmationService _confirmation;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportError;

    public PluginItemViewModel(InstalledPlugin plugin, PluginCatalog catalog, IWindowNavigationService navigation, IConfirmationService confirmation, Func<Task> refresh, Action<string> reportError)
    {
        _plugin = plugin;
        _catalog = catalog;
        _navigation = navigation;
        _confirmation = confirmation;
        _refresh = refresh;
        _reportError = reportError;
        _isEnabled = plugin.IsEnabled;
        LoadTools();
    }

    public string Name => _plugin.Manifest.Name;
    public string Id => _plugin.Id;
    public string Version => _plugin.Manifest.Version;
    public string? Description => _plugin.Error ?? _plugin.Manifest.Description;
    public bool HasError => _plugin.Error is not null;
    public bool CanToggle => !HasError;
    public bool CanRemove => !HasError;
    public ObservableCollection<PluginToolItemViewModel> Tools { get; } = [];

    [ObservableProperty] private bool _isEnabled;

    public void LoadTools()
    {
        Tools.Clear();
        foreach (var tool in _plugin.Manifest.Tools)
            Tools.Add(new PluginToolItemViewModel(_plugin.Id, tool, _navigation, IsEnabled && !HasError));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (HasError) { IsEnabled = false; return; }
        foreach (var tool in Tools) tool.IsAvailable = value;
        if (!value) _navigation.ClosePluginTools(Id);
        OnPropertyChanged(nameof(CanToggle));
        _ = PersistEnabledAsync(value);
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        try
        {
            if (!await _confirmation.ConfirmDeleteWithWarningAsync(Name, "插件定义将被移除，插件数据会保留。")) return;
            _navigation.ClosePluginTools(Id);
            await _catalog.RemoveAsync(Id);
            await _refresh();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginItemViewModel), $"Could not remove plugin '{Id}'", LogLevel.Warning);
            _reportError(exception.Message);
        }
    }

    private async Task PersistEnabledAsync(bool value)
    {
        try { await _catalog.SetEnabledAsync(Id, value); }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginItemViewModel), $"Could not update enabled state for plugin '{Id}'", LogLevel.Warning);
            IsEnabled = !value;
            _reportError(exception.Message);
        }
    }
}

public sealed partial class PluginToolItemViewModel(string pluginId, PluginToolDefinition tool, IWindowNavigationService navigation, bool isAvailable) : ObservableObject
{
    public string Name => tool.Name;
    public string Id => tool.Id;
    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => navigation.ShowPluginTool(pluginId, tool.Id));
    [ObservableProperty] private bool _isAvailable = isAvailable;
}
