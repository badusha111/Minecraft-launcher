using System.Text.Json;
using NexcraftLauncher.Models;

namespace NexcraftLauncher.Services;

/// <summary>
/// Validates manifest metadata before any future installer writes files.
/// This does not download or execute anything.
/// </summary>
public static class ModpackManifestValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ModpackManifest ParseAndValidate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var manifest = JsonSerializer.Deserialize<ModpackManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Manifest JSON is empty or invalid.");

        if (manifest.ManifestVersion != 1)
            throw new InvalidDataException($"Unsupported manifest version: {manifest.ManifestVersion}.");

        if (!IsSafeId(manifest.PackId))
            throw new InvalidDataException("Pack ID may contain only letters, digits, '-' and '_'.");

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
            throw new InvalidDataException("Pack display name is required.");

        if (string.IsNullOrWhiteSpace(manifest.MinecraftVersion))
            throw new InvalidDataException("Minecraft version is required.");

        if (manifest.Files.Count > 2000)
            throw new InvalidDataException("Manifest contains too many files.");

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path);

            if (!seenPaths.Add(file.Path.Replace('\\', '/')))
                throw new InvalidDataException($"Duplicate file path: {file.Path}");

            if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"File URL must use HTTPS: {file.Path}");

            if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Invalid SHA-256 hash for: {file.Path}");
        }

        return manifest;
    }

    private static bool IsSafeId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Every file needs a relative path.");

        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) ||
            normalized.StartsWith('/') ||
            normalized.Split('/').Any(part => part is ".." or "." or ""))
        {
            throw new InvalidDataException($"Unsafe relative file path: {path}");
        }

        if (normalized.Contains(':'))
            throw new InvalidDataException($"Unsafe file path: {path}");
    }
}