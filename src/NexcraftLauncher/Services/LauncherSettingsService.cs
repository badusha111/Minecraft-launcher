using System.Text.Json;
using NexcraftLauncher.Models;

namespace NexcraftLauncher.Services;

public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NEXCRAFT",
        "settings.json");

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new LauncherSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, Options);
            return Normalize(settings ?? new LauncherSettings());
        }
        catch (IOException)
        {
            return new LauncherSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
        catch (JsonException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("Could not determine settings directory.");

        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, Options));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    private static LauncherSettings Normalize(LauncherSettings settings)
    {
        settings.MemoryMinimumMb = Math.Clamp(settings.MemoryMinimumMb, 512, 65536);
        settings.MemoryMaximumMb = Math.Clamp(settings.MemoryMaximumMb, settings.MemoryMinimumMb, 65536);

        if (string.IsNullOrWhiteSpace(settings.SelectedMinecraftVersion))
            settings.SelectedMinecraftVersion = "1.21.1";

        if (string.IsNullOrWhiteSpace(settings.GameDirectory))
        {
            settings.GameDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXCRAFT",
                "instances",
                "default");
        }

        return settings;
    }
}