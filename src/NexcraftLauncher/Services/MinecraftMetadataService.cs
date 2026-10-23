using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexcraftLauncher.Services;

public sealed record MinecraftVersionEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string Url);

public sealed record MinecraftVersionManifest(
    [property: JsonPropertyName("latest")] MinecraftLatestVersions Latest,
    [property: JsonPropertyName("versions")] List<MinecraftVersionEntry> Versions);

public sealed record MinecraftLatestVersions(
    [property: JsonPropertyName("release")] string Release,
    [property: JsonPropertyName("snapshot")] string Snapshot);

public sealed record MinecraftClientDownload(
    [property: JsonPropertyName("sha1")] string Sha1,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("url")] string Url);

public sealed record MinecraftVersionMetadata(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("mainClass")] string MainClass,
    [property: JsonPropertyName("javaVersion")] MinecraftJavaRequirement? JavaVersion,
    [property: JsonPropertyName("downloads")] Dictionary<string, MinecraftClientDownload> Downloads);

public sealed record MinecraftJavaRequirement(
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("majorVersion")] int MajorVersion);

/// <summary>Reads official Mojang version metadata over HTTPS. Does not download game binaries.</summary>
public sealed class MinecraftMetadataService
{
    private static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private const int MaximumManifestBytes = 8 * 1024 * 1024;
    private const int MaximumVersionMetadataBytes = 4 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public MinecraftMetadataService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<MinecraftVersionManifest> GetVersionManifestAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await GetJsonAsync<MinecraftVersionManifest>(ManifestUri, MaximumManifestBytes, cancellationToken);
        if (manifest.Versions is null || manifest.Versions.Count == 0)
            throw new InvalidDataException("Mojang returned an empty version manifest.");
        return manifest;
    }

    public async Task<MinecraftVersionMetadata> GetVersionMetadataAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        var manifest = await GetVersionManifestAsync(cancellationToken);
        var entry = manifest.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Minecraft version '{versionId}' was not found in the official manifest.");

        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var metadataUri) ||
            metadataUri.Scheme != Uri.UriSchemeHttps ||
            !(string.Equals(metadataUri.Host, "mojang.com", StringComparison.OrdinalIgnoreCase) || metadataUri.Host.EndsWith(".mojang.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The official version metadata URL is invalid.");

        var metadata = await GetJsonAsync<MinecraftVersionMetadata>(
            metadataUri, MaximumVersionMetadataBytes, cancellationToken);

        if (!string.Equals(metadata.Id, versionId, StringComparison.Ordinal))
            throw new InvalidDataException("The downloaded version metadata ID does not match the requested version.");

        if (metadata.Downloads is null ||
            !metadata.Downloads.TryGetValue("client", out var client) ||
            !Uri.TryCreate(client.Url, UriKind.Absolute, out var clientUri) ||
            clientUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(client.Sha1) ||
            client.Sha1.Length != 40 ||
            !client.Sha1.All(Uri.IsHexDigit) ||
            client.Size <= 0)
            throw new InvalidDataException("The official client download metadata is incomplete or invalid.");

        return metadata;
    }

    private async Task<T> GetJsonAsync<T>(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Metadata redirects must remain on HTTPS.");

        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("Official metadata exceeded the permitted size.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException("Official metadata exceeded the permitted size.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var result = JsonSerializer.Deserialize<T>(memory.ToArray())
            ?? throw new InvalidDataException("Official metadata JSON was empty or invalid.");
        return result;
    }
}
