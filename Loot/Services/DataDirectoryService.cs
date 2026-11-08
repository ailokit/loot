namespace Loot.Services;

/// <summary>Changes the data directory used on the next application start.</summary>
public sealed class DataDirectoryService(AppPaths currentPaths, DataDirectoryConfiguration configuration)
{
    public string CurrentDataDirectory => currentPaths.ApplicationDataDirectory;

    public void SetDataDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        configuration.SaveDataDirectory(Path.GetFullPath(dataDirectory));
    }
}
