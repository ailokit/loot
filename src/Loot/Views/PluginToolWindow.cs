using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Loot.Plugins;
using Loot.Services;
using Loot.JsonRender.Avalonia;
using Loot.Logging;
using Microsoft.Extensions.Logging;

namespace Loot.Views;

internal sealed class PluginToolWindow : LootWindow
{
    private readonly PluginCatalog _catalog;
    private readonly IPluginScriptRuntimeFactory _runtimeFactory;
    private readonly StackPanel _content;
    private readonly TextBlock _status;
    private readonly JsonObject _state = new();
    private IPluginScriptRuntime? _runtime;
    private string _uiJson = string.Empty;
    private Control? _renderedUi;

    public string PluginId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    protected override bool HideOnClose => false;

    public PluginToolWindow(PluginCatalog catalog, IPluginScriptRuntimeFactory runtimeFactory)
    {
        _catalog = catalog;
        _runtimeFactory = runtimeFactory;
        _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brushes.IndianRed };
        _content = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(20) };
        Content = new ScrollViewer { Content = _content };
        Width = 520;
        Height = 600;
        MinWidth = 360;
        MinHeight = 320;
    }

    protected override async void OnOpened(EventArgs e)
    {
        try
        {
            base.OnOpened(e);
            var plugin = await _catalog.FindAsync(PluginId);
            if (plugin is null || plugin.Error is not null || !plugin.IsEnabled)
                throw new InvalidOperationException("插件不存在、无效或尚未启用。");
            var tool = plugin.Manifest.Tools.FirstOrDefault(item => string.Equals(item.Id, ToolId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("找不到该小工具。");
            var uiPath = _catalog.ResolvePackagePath(plugin.DirectoryPath, tool.Ui);
            var scriptPath = _catalog.ResolvePackagePath(plugin.DirectoryPath, tool.Entry);
            var ui = await ReadLimitedAsync(uiPath, JsonRenderRenderer.MaximumSpecBytes);
            _uiJson = ui;
            var script = await ReadLimitedAsync(scriptPath, 2 * 1024 * 1024);

            Title = $"{tool.Name} - {plugin.Manifest.Name}";
            _content.Children.Clear();
            _content.Children.Add(new TextBlock { Text = tool.Name, FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            _content.Children.Add(_status);
            _runtime = _runtimeFactory.Create();
            _runtime.Message += OnRuntimeMessage;
            try
            {
                await _runtime.StartAsync(script, scriptPath);
                _status.Text = string.Empty;
            }
            catch (Exception exception)
            {
                ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin script failed to start ({PluginId}/{ToolId})", LogLevel.Warning);
                _status.Text = $"脚本启动失败：{exception.Message}";
            }
            BuildUi(ui);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin tool failed to open ({PluginId}/{ToolId})", LogLevel.Warning);
            _content.Children.Clear();
            _content.Children.Add(new TextBlock { Text = exception.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brushes.IndianRed });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_runtime is not null)
        {
            _runtime.Message -= OnRuntimeMessage;
            _ = DisposeRuntimeSafelyAsync(_runtime);
            _runtime = null;
        }
        base.OnClosed(e);
    }

    private void BuildUi(string json)
    {
        try
        {
            var view = JsonRenderRenderer.Render(json, _state, OnUiAction, OnRendererStateChanged, errorSink: exception =>
            {
                ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin UI event failed ({PluginId}/{ToolId})", LogLevel.Warning);
                ReportStatus($"插件操作失败：{exception.Message}");
            });
            if (_renderedUi is not null)
                _content.Children.Remove(_renderedUi);
            _renderedUi = view;
            _content.Children.Add(view);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin UI rendering failed ({PluginId}/{ToolId})", LogLevel.Warning);
            _status.Text = $"UI 描述无效：{exception.Message}";
        }
    }

    private void OnRendererStateChanged()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { if (!string.IsNullOrEmpty(_uiJson)) BuildUi(_uiJson); }
                catch (Exception exception) { ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin UI refresh failed ({PluginId}/{ToolId})", LogLevel.Warning); }
            });
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin UI refresh failed ({PluginId}/{ToolId})", LogLevel.Warning);
        }
    }

    private void OnUiAction(string action, JsonObject payload)
    {
        if (_runtime is null) { _status.Text = "QuickJS-NG 原生运行时不可用，无法执行插件逻辑。"; return; }
        _ = DispatchActionAsync(action, payload);
    }

    private async Task DispatchActionAsync(string action, JsonObject payload)
    {
        try { await _runtime!.DispatchActionAsync(action, payload); }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin action '{action}' failed ({PluginId}/{ToolId})", LogLevel.Warning);
            ReportStatus(exception.Message);
        }
    }

    private void OnRuntimeMessage(object? sender, PluginRuntimeMessageEventArgs e)
    {
        if (e.Name == "notify")
        {
            try { Dispatcher.UIThread.Post(() => _status.Text = e.Payload?.ToString() ?? string.Empty); }
            catch (Exception exception) { Loot.Logging.ExceptionLogger.Log(exception, nameof(PluginToolWindow), "Could not display a plugin notification"); }
            return;
        }
        if (e.Name != "setState" || e.Payload is not JsonObject patch) return;
        try { Dispatcher.UIThread.Post(() =>
        {
            try
            {
                foreach (var pair in patch)
                    _state[pair.Key] = pair.Value?.DeepClone();
                if (!string.IsNullOrEmpty(_uiJson)) BuildUi(_uiJson);
            }
            catch (Exception exception)
            {
                ExceptionLogger.Log(exception, nameof(PluginToolWindow), $"Plugin state update failed ({PluginId}/{ToolId})", LogLevel.Warning);
                _status.Text = $"插件状态更新失败：{exception.Message}";
            }
        }); }
        catch (Exception exception) { Loot.Logging.ExceptionLogger.Log(exception, nameof(PluginToolWindow), "Could not dispatch a plugin state update"); }
    }

    private void ReportStatus(string message)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) _status.Text = message;
            else Dispatcher.UIThread.Post(() => _status.Text = message);
        }
        catch (Exception exception) { Loot.Logging.ExceptionLogger.Log(exception, nameof(PluginToolWindow), "Could not display a plugin error"); }
    }

    private static async Task DisposeRuntimeSafelyAsync(IPluginScriptRuntime runtime)
    {
        try { await runtime.DisposeAsync(); }
        catch (Exception exception) { Loot.Logging.ExceptionLogger.Log(exception, nameof(PluginToolWindow), "Could not dispose plugin runtime"); }
    }

    private static async Task<string> ReadLimitedAsync(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("插件文件不存在。", path);
        if (info.Length > maximumBytes) throw new InvalidDataException($"插件文件超过 {maximumBytes} 字节限制。");
        return await File.ReadAllTextAsync(path);
    }
}
