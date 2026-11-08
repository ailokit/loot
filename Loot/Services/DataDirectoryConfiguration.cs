namespace Loot.Services;

/// <summary>
/// Stores only the bootstrap pointer to the user-selected data directory. The actual user data remains in
/// <see cref="AppPaths.ApplicationDataDirectory"/>, allowing the pointer to be read before SQLite is opened.
/// </summary>
public sealed class DataDirectoryConfiguration
{
    private readonly string _configurationPath;
    private readonly string? _legacyConfigurationPath;

    public DataDirectoryConfiguration(string configurationPath, string? legacyConfigurationPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _configurationPath = Path.GetFullPath(configurationPath);
        _legacyConfigurationPath = string.IsNullOrWhiteSpace(legacyConfigurationPath)
            ? null
            : Path.GetFullPath(legacyConfigurationPath);
    }

    public static DataDirectoryConfiguration CreateDefault() =>
        new(AppPaths.GetDataDirectoryConfigurationPath(), AppPaths.GetLegacyDataDirectoryConfigurationPath());

    public string? GetDataDirectory()
    {
        return TryRead(_configurationPath) ??
               (_legacyConfigurationPath is null ? null : TryRead(_legacyConfigurationPath));
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var dataDirectory = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.GetFullPath(dataDirectory);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public void SaveDataDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var directory = Path.GetDirectoryName(_configurationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_configurationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, Path.GetFullPath(dataDirectory));
            File.Move(temporaryPath, _configurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
