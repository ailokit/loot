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

    public string DefaultWorkspaceDirectory => Path.Combine(ApplicationDataDirectory, "default-workspace");

    public static AppPaths CreateDefault()
    {
        var configuration = DataDirectoryConfiguration.CreateDefault();
        var configuredDirectory = configuration.GetDataDirectory();
        var dataDirectory = configuredDirectory ?? GetDefaultDataDirectory();
        var paths = new AppPaths(dataDirectory);

        if (configuredDirectory is null)
        {
            MigrateLegacyDefaultDirectory(paths);
        }

        return paths;
    }

    public static string GetDefaultDataDirectory() => Path.Combine(GetUserApplicationDataRoot(), ApplicationName);

    internal static string GetDataDirectoryConfigurationPath() =>
        Path.Combine(GetUserApplicationDataRoot(), $"{ApplicationName}.data-directory");

    internal static string GetLegacyDataDirectoryConfigurationPath() =>
        Path.Combine(GetUserApplicationDataRoot(), $"{GetLegacyApplicationName()}.data-directory");

    private static string GetLegacyDefaultDataDirectory() =>
        Path.Combine(GetUserApplicationDataRoot(), GetLegacyApplicationName());

    private static string GetLegacyApplicationName()
    {
#if DEBUG
        return "Ailo-dev";
#else
        return "Ailo";
#endif
    }

    private static void MigrateLegacyDefaultDirectory(AppPaths paths)
    {
        var legacyDirectory = GetLegacyDefaultDataDirectory();
        var migrationMarker = Path.Combine(paths.ApplicationDataDirectory, ".legacy-ailo-data-copied");
        if (!File.Exists(migrationMarker) &&
            Directory.Exists(legacyDirectory) &&
            !string.Equals(legacyDirectory, paths.ApplicationDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            CopyDirectory(legacyDirectory, paths.ApplicationDataDirectory);
            File.WriteAllText(migrationMarker, DateTimeOffset.UtcNow.ToString("O"));
        }

        paths.MigrateLegacyFiles();
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (!File.Exists(destinationFile))
            {
                File.Copy(sourceFile, destinationFile);
            }
        }
    }

    internal static string GetUserApplicationDataRoot() => OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        MigrateLegacyFiles();
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(SkillsDirectory);
        Directory.CreateDirectory(DefaultWorkspaceDirectory);
    }

    private void MigrateLegacyFiles()
    {
        MoveIfDestinationMissing(Path.Combine(ApplicationDataDirectory, "ailo.db"), DatabasePath);
        MoveIfDestinationMissing(Path.Combine(ApplicationDataDirectory, "ailo.db-wal"), $"{DatabasePath}-wal");
        MoveIfDestinationMissing(Path.Combine(ApplicationDataDirectory, "ailo.db-shm"), $"{DatabasePath}-shm");

        var legacyLogsDirectory = Path.Combine(ApplicationDataDirectory, "logs");
        if (!Directory.Exists(legacyLogsDirectory))
        {
            return;
        }

        foreach (var legacyLog in Directory.EnumerateFiles(legacyLogsDirectory, "ailo-*"))
        {
            var newLog = Path.Combine(legacyLogsDirectory, $"loot-{Path.GetFileName(legacyLog)[5..]}");
            MoveIfDestinationMissing(legacyLog, newLog);
        }
    }

    private static void MoveIfDestinationMissing(string source, string destination)
    {
        if (File.Exists(source) && !File.Exists(destination))
        {
            File.Move(source, destination);
        }
    }
}
