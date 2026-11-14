namespace Loot.Services;

public sealed class AppPaths
{
    public const string ApplicationName = AppIdentity.ApplicationName;

    public AppPaths(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
        LogsDirectory = Path.Combine(ApplicationDataDirectory, "logs");
    }

    public string ApplicationDataDirectory { get; }

    public string DatabasePath => Path.Combine(ApplicationDataDirectory, "loot.db");

    public string LogsDirectory { get; }

    public string ExportsDirectory => Path.Combine(ApplicationDataDirectory, "exports");

    public string AttachmentsDirectory => Path.Combine(ApplicationDataDirectory, "attachments");

    /// <summary>Root for portable, file-based Agent Skills owned by this application.</summary>
    public string SkillsDirectory => Path.Combine(ApplicationDataDirectory, "skills");

    /// <summary>Stores only local skill availability overrides; skill definitions always come from disk.</summary>
    public string SkillsAvailabilityPath => Path.Combine(ApplicationDataDirectory, "skills-availability.json");

    /// <summary>Root for installed Loot plugin packages.</summary>
    public string PluginsDirectory => Path.Combine(ApplicationDataDirectory, "plugins");

    /// <summary>Host-owned persistent data for plugins, kept separate from installed package files.</summary>
    public string PluginsDataDirectory => Path.Combine(ApplicationDataDirectory, "plugins-data");

    public string PluginsStatePath => Path.Combine(ApplicationDataDirectory, "plugins-state.json");

    public string DefaultWorkspaceDirectory => Path.Combine(ApplicationDataDirectory, "default-workspace");

    public static AppPaths CreateDefault()
    {
        var configuration = DataDirectoryConfiguration.CreateDefault();
        var configuredDirectory = configuration.GetDataDirectory();
        var dataDirectory = configuredDirectory ?? GetDefaultDataDirectory();
        return new AppPaths(dataDirectory);
    }

    public static string GetDefaultDataDirectory() => Path.Combine(GetUserApplicationDataRoot(), ApplicationName);

    internal static string GetDataDirectoryConfigurationPath() =>
        Path.Combine(GetUserApplicationDataRoot(), $"{ApplicationName}.data-directory");

    internal static string GetUserApplicationDataRoot() => OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(SkillsDirectory);
        Directory.CreateDirectory(PluginsDirectory);
        Directory.CreateDirectory(PluginsDataDirectory);
        Directory.CreateDirectory(DefaultWorkspaceDirectory);
    }

    public string GetPluginDataDirectory(string pluginId)
    {
        Loot.Plugins.PluginCatalog.ValidateId(pluginId, "plugin");
        return Path.Combine(PluginsDataDirectory, pluginId);
    }

}
