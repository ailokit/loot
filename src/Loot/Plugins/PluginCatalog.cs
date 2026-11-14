using System.Text.Json;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Loot.Logging;
using Loot.Services;
using Microsoft.Extensions.Logging;

namespace Loot.Plugins;

public sealed partial class PluginCatalog(AppPaths paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginStateDocument _state = new();

    public async Task<IReadOnlyList<InstalledPlugin>> ScanAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureCreated();
            _state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            var plugins = new List<InstalledPlugin>();
            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.EnumerateDirectories(paths.PluginsDirectory).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(directory);
                if (id.StartsWith(".", StringComparison.Ordinal)) continue;
                try
                {
                    EnsureSafeDirectory(directory);
                    var manifestPath = ResolvePackagePath(directory, "plugin.json");
                    var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                    if (bytes.Length > 256 * 1024) throw new InvalidDataException("plugin.json exceeds 256 KiB.");
                    var manifest = DeserializeManifest(bytes);
                    ValidateManifest(manifest, directory);
                    if (!knownIds.Add(manifest.Id)) throw new InvalidDataException($"Duplicate plugin id '{manifest.Id}'.");
                    var enabled = _state.Plugins.TryGetValue(manifest.Id, out var saved) && saved.Enabled;
                    plugins.Add(new InstalledPlugin(directory, manifest, enabled));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
                {
                    ExceptionLogger.Log(exception, nameof(PluginCatalog), $"Plugin package '{id}' could not be loaded", LogLevel.Warning);
                    plugins.Add(new InstalledPlugin(directory, new PluginManifest { Id = id, Name = id }, false, exception.Message));
                }
            }
            return plugins;
        }
        finally { _gate.Release(); }
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
    {
        ValidateId(pluginId, "plugin");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            if (!_state.Plugins.TryGetValue(pluginId, out var entry))
                _state.Plugins[pluginId] = entry = new PluginStateEntry();
            entry.Enabled = enabled;
            await WriteStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginCatalog), $"Could not change enabled state for plugin '{pluginId}'", LogLevel.Warning);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<InstalledPlugin?> FindAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        var plugins = await ScanAsync(cancellationToken).ConfigureAwait(false);
        return plugins.FirstOrDefault(plugin => string.Equals(plugin.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task InstallFromDirectoryAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await InstallFromDirectoryCoreAsync(sourceDirectory, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginCatalog), $"Could not install plugin from '{sourceDirectory}'", LogLevel.Warning);
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task InstallFromDirectoryCoreAsync(string sourceDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var source = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        EnsureSafeDirectory(source);

        var manifestPath = Path.Combine(source, "plugin.json");
        if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("plugin.json cannot be a symbolic link.");
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (manifestBytes.Length > 256 * 1024) throw new InvalidDataException("plugin.json exceeds 256 KiB.");
        var manifest = DeserializeManifest(manifestBytes);
        ValidateManifest(manifest, Path.Combine(paths.PluginsDirectory, manifest.Id), validateFiles: false);

        paths.EnsureCreated();
        var destination = Path.Combine(paths.PluginsDirectory, manifest.Id);
        if (Directory.Exists(destination) || File.Exists(destination) ||
            Directory.EnumerateDirectories(paths.PluginsDirectory).Any(path => string.Equals(Path.GetFileName(path), manifest.Id, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"Plugin '{manifest.Id}' is already installed.");
        var staging = Path.Combine(paths.PluginsDirectory, $".{manifest.Id}.{Guid.NewGuid():N}.staging");
        try
        {
            Directory.CreateDirectory(staging);
            long totalBytes = 0;
            var files = EnumerateSafeFiles(source).ToArray();
            if (files.Length > 2000) throw new InvalidDataException("Plugin package contains more than 2000 files.");
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Plugin packages cannot contain symbolic links.");
                var relative = Path.GetRelativePath(source, file);
                var target = ResolvePackagePath(staging, relative);
                var info = new FileInfo(file);
                totalBytes += info.Length;
                if (totalBytes > 50 * 1024 * 1024) throw new InvalidDataException("Plugin package exceeds 50 MiB.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = File.OpenRead(file);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            var stagedManifest = await File.ReadAllBytesAsync(Path.Combine(staging, "plugin.json"), cancellationToken).ConfigureAwait(false);
            var copiedManifest = DeserializeManifest(stagedManifest);
            ValidateManifest(copiedManifest, staging, validateFiles: true, validateDirectoryName: false);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public async Task InstallFromArchiveAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Plugin archive was not found.", fullPath);
        var importRoot = Path.Combine(paths.PluginsDataDirectory, ".imports");
        Directory.CreateDirectory(importRoot);
        var extraction = Path.Combine(importRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraction);
        try
        {
            await using var file = File.OpenRead(fullPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is 0 or > 2000) throw new InvalidDataException("Plugin archive must contain between 1 and 2000 entries.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = entry.FullName.Replace('\\', '/');
                var isDirectory = relative.EndsWith("/", StringComparison.Ordinal);
                relative = relative.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(relative) || !seen.Add(relative))
                    throw new InvalidDataException("Plugin archive contains an empty or duplicate path.");
                var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
                var windowsAttributes = entry.ExternalAttributes & 0xFFFF;
                if (unixFileType == 0xA000 || (windowsAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Plugin archives cannot contain symbolic links.");
                var target = ResolvePackagePath(extraction, relative);
                if (isDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                totalBytes += entry.Length;
                if (totalBytes > 50 * 1024 * 1024) throw new InvalidDataException("Plugin archive expands beyond 50 MiB.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                if (output.Length != entry.Length) throw new InvalidDataException("Plugin archive entry size did not match its directory record.");
            }
            await InstallFromDirectoryAsync(extraction, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginCatalog), $"Could not install plugin archive '{fullPath}'", LogLevel.Warning);
            throw;
        }
        finally
        {
            if (Directory.Exists(extraction)) Directory.Delete(extraction, recursive: true);
        }
    }

    public async Task RemoveAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ValidateId(pluginId, "plugin");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(paths.PluginsDirectory, pluginId);
            if (Directory.Exists(directory))
            {
                EnsureSafeDirectory(directory);
                var trashRoot = Path.Combine(paths.PluginsDataDirectory, ".removed-packages");
                Directory.CreateDirectory(trashRoot);
                var trash = Path.Combine(trashRoot, $"{pluginId}.{Guid.NewGuid():N}");
                Directory.Move(directory, trash);
                try { Directory.Delete(trash, recursive: true); }
                catch { /* Package removal succeeded; leave staged files for later cleanup. */ }
            }
            _state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            _state.Plugins.Remove(pluginId);
            await WriteStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginCatalog), $"Could not remove plugin '{pluginId}'", LogLevel.Warning);
            throw;
        }
        finally { _gate.Release(); }
    }

    public string ResolvePackagePath(string pluginDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Package paths must be non-empty and relative.");
        var root = Path.GetFullPath(pluginDirectory) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Package path '{relativePath}' escapes the plugin directory.");
        var current = Path.GetFullPath(pluginDirectory);
        foreach (var segment in relativePath.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or "." or "..") throw new InvalidDataException($"Package path '{relativePath}' is invalid.");
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Package path '{relativePath}' traverses a symbolic link.");
            }
        }
        return target;
    }

    private static void EnsureSafeDirectory(string directory)
    {
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Plugin directory cannot be a symbolic link.");
    }

    private void ValidateManifest(
        PluginManifest manifest,
        string directory,
        bool validateFiles = true,
        bool validateDirectoryName = true)
    {
        if (manifest.ManifestVersion != 1) throw new InvalidDataException($"Unsupported manifestVersion {manifest.ManifestVersion}.");
        ValidateId(manifest.Id, "plugin");
        if (validateDirectoryName && !string.Equals(Path.GetFileName(directory), manifest.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin directory name must match manifest id.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || !Version.TryParse(manifest.Version, out _))
            throw new InvalidDataException("Manifest requires a name and numeric version.");
        if (manifest.Runtime?.ApiVersion != 1) throw new InvalidDataException("Unsupported or missing plugin runtime API version.");
        if (!IsLootVersionCompatible(manifest.Loot)) throw new InvalidDataException("This plugin is not compatible with the installed Loot version.");
        if (manifest.Capabilities is null || manifest.Capabilities.Count > 0) throw new InvalidDataException("This Loot build does not support requested plugin capabilities.");
        if (manifest.Tools is not { Count: > 0 }) throw new InvalidDataException("Plugin must declare at least one tool.");
        var toolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in manifest.Tools)
        {
            if (tool is null) throw new InvalidDataException("Tool entries cannot be null.");
            ValidateId(tool.Id, "tool");
            if (!toolIds.Add(tool.Id)) throw new InvalidDataException($"Duplicate tool id '{tool.Id}'.");
            if (string.IsNullOrWhiteSpace(tool.Name)) throw new InvalidDataException($"Tool '{tool.Id}' requires a name.");
            if (string.IsNullOrWhiteSpace(tool.Ui) || string.IsNullOrWhiteSpace(tool.Entry)) throw new InvalidDataException($"Tool '{tool.Id}' requires ui and entry paths.");
            if (validateFiles)
            {
                var uiPath = ResolvePackagePath(directory, tool.Ui);
                var entryPath = ResolvePackagePath(directory, tool.Entry);
                if (!File.Exists(uiPath) || !File.Exists(entryPath))
                    throw new InvalidDataException($"Tool '{tool.Id}' references a missing UI or JavaScript entry file.");
                if (tool.Icon is not null && !File.Exists(ResolvePackagePath(directory, tool.Icon)))
                    throw new InvalidDataException($"Tool '{tool.Id}' references a missing icon.");
            }
        }
    }

    private static PluginManifest DeserializeManifest(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        ValidateUniqueProperties(document.RootElement, "plugin.json");
        return document.RootElement.Deserialize(PluginJsonContext.Default.PluginManifest)
            ?? throw new InvalidDataException("plugin.json is empty.");
    }

    private static void ValidateUniqueProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property '{property.Name}' in {path}.");
                ValidateUniqueProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) ValidateUniqueProperties(item, $"{path}[{index++}]");
        }
    }

    private static bool IsLootVersionCompatible(LootCompatibility? compatibility)
    {
        if (compatibility is null) return true;
        var current = typeof(PluginCatalog).Assembly.GetName().Version ?? new Version(0, 0);
        if (!string.IsNullOrWhiteSpace(compatibility.MinVersion))
        {
            if (!Version.TryParse(compatibility.MinVersion, out var minimum) || current < minimum) return false;
        }
        if (string.IsNullOrWhiteSpace(compatibility.MaxVersion)) return true;
        var maximumText = compatibility.MaxVersion.Trim();
        if (!maximumText.EndsWith(".x", StringComparison.OrdinalIgnoreCase))
            return Version.TryParse(maximumText, out var maximum) && current <= maximum;
        var parts = maximumText[..^2].Split('.');
        var currentParts = new[] { current.Major, current.Minor, current.Build };
        if (parts.Length is < 1 or > 2) return false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var expected) || currentParts[index] != expected) return false;
        }
        return true;
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Plugin packages cannot contain symbolic links.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    public static void ValidateId(string id, string kind)
    {
        if (string.IsNullOrWhiteSpace(id) || !PluginIdPattern().IsMatch(id))
            throw new InvalidDataException($"Invalid {kind} id '{id}'.");
    }

    private async Task<PluginStateDocument> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.PluginsStatePath)) return new PluginStateDocument();
        try
        {
            await using var stream = File.OpenRead(paths.PluginsStatePath);
            var state = await JsonSerializer.DeserializeAsync(stream, PluginJsonContext.Default.PluginStateDocument, cancellationToken).ConfigureAwait(false) ?? new PluginStateDocument();
            return state.Plugins is null ? new PluginStateDocument() : state;
        }
        catch (JsonException exception)
        {
            ExceptionLogger.Log(exception, nameof(PluginCatalog), "Plugin state file is invalid; using default plugin state", LogLevel.Warning);
            return new PluginStateDocument();
        }
    }

    private async Task WriteStateAsync(CancellationToken cancellationToken)
    {
        var temporaryPath = $"{paths.PluginsStatePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, _state, PluginJsonContext.Default.PluginStateDocument, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, paths.PluginsStatePath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();
}
