using System.Text.Json.Serialization;

namespace NexcraftLauncher.Models;

/// <summary>
/// Describes a server-approved modpack. Files are downloaded only from HTTPS
/// URLs and their SHA-256 hashes are checked before installation.
/// </summary>
public sealed class ModpackManifest
{
    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; init; } = 1;

    [JsonPropertyName("packId")]
    public string PackId { get; init; } = "nexcraft-default";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "NEXCRAFT Server Pack";

    [JsonPropertyName("minecraftVersion")]
    public string MinecraftVersion { get; init; } = "1.21.1";

    [JsonPropertyName("loader")]
    public string Loader { get; init; } = "vanilla";

    [JsonPropertyName("loaderVersion")]
    public string? LoaderVersion { get; init; }

    [JsonPropertyName("files")]
    public List<ModpackFile> Files { get; init; } = [];
}

public sealed class ModpackFile
{
    /// <summary>Path relative to the instance directory, e.g. mods/example.jar.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>Expected lowercase or uppercase SHA-256 hex digest (64 characters).</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;
}