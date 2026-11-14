using Loot.Services;

namespace Loot.Tests;

public sealed class DataDirectoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Loot.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetDataDirectory_ChangesTheStartupSelectionWithoutCopyingData()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        var destinationDirectory = Path.Combine(_root, "destination");
        var paths = new AppPaths(sourceDirectory);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(Path.Combine(paths.ApplicationDataDirectory, "existing.txt"), "existing data");
        var configuration = new DataDirectoryConfiguration(Path.Combine(_root, "bootstrap", "data-directory"));
        var service = new DataDirectoryService(paths, configuration);

        service.SetDataDirectory(destinationDirectory);

        Assert.Equal(Path.GetFullPath(destinationDirectory), configuration.GetDataDirectory());
        Assert.False(Directory.Exists(destinationDirectory));
        Assert.Equal("existing data", await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "existing.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
