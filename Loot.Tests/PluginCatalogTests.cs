using System.IO.Compression;
using Loot.Plugins;
using Loot.Services;

namespace Loot.Tests;

public sealed class PluginCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Loot.PluginTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallFromDirectory_InstallsAndDiscoversExamplePlugin()
    {
        var catalog = new PluginCatalog(new AppPaths(Path.Combine(_root, "data")));
        var source = FindExamplePluginDirectory();

        await catalog.InstallFromDirectoryAsync(source);

        var plugin = Assert.Single(await catalog.ScanAsync());
        Assert.Equal("loot.example.hello", plugin.Id);
        Assert.Null(plugin.Error);
        Assert.False(plugin.IsEnabled);
        Assert.True(File.Exists(Path.Combine(plugin.DirectoryPath, "tools", "hello", "ui.json")));
        Assert.True(File.Exists(Path.Combine(plugin.DirectoryPath, "tools", "hello", "main.js")));

        await catalog.SetEnabledAsync(plugin.Id, true);
        Assert.True((await catalog.FindAsync(plugin.Id))?.IsEnabled);
    }

    [Fact]
    public async Task InstallFromArchive_InstallsAndDiscoversExamplePlugin()
    {
        var source = FindExamplePluginDirectory();
        var archivePath = Path.Combine(_root, "hello.lootplugin");
        Directory.CreateDirectory(_root);
        ZipFile.CreateFromDirectory(source, archivePath);
        var catalog = new PluginCatalog(new AppPaths(Path.Combine(_root, "archive-data")));

        await catalog.InstallFromArchiveAsync(archivePath);

        var plugin = Assert.Single(await catalog.ScanAsync());
        Assert.Equal("loot.example.hello", plugin.Id);
        Assert.Null(plugin.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string FindExamplePluginDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "examples", "plugins", "hello");
            if (File.Exists(Path.Combine(candidate, "plugin.json"))) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate examples/plugins/hello from the test directory.");
    }
}
