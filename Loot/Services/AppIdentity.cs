namespace Loot.Services;

/// <summary>
/// Separates local development instances from distributed application instances.
/// This prevents their operating-system permissions, startup registrations, and
/// user data from sharing an identity.
/// </summary>
public static class AppIdentity
{
    public const string DisplayName = "千机";

#if DEBUG
    public const string ApplicationName = "Loot-dev";
    public const string MacBundleIdentifier = "com.loot.app.dev";
#else
    public const string ApplicationName = "Loot";
    public const string MacBundleIdentifier = "com.loot.app";
#endif
}
