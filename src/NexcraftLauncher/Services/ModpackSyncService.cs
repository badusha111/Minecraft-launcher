using System.Net.Http;
using System.Text;
using NexcraftLauncher.Models;

namespace NexcraftLauncher.Services;

/// <summary>
/// Fetches and validates a remote pack manifest, then installs its files one by one.
/// All downloads use HTTPS and each payload is hash-verified by ModpackInstallerService.
/// </summary>
public sealed class ModpackSyncService
{
    private const int MaximumManifestBytes = 2 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly ModpackInstallerService _installer;

    public ModpackSyncService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _installer = new ModpackInstallerService(_httpClient);
    }

    public async Task<ModpackManifest> DownloadAndInstallAsync(
        string manifestUrl,
        string instanceDirectory,
        IProgress<ModpackInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Manifest URL must use HTTPS.");

        using var response = await _httpClient.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Manifest redirects must remain on HTTPS.");

        if (response.Content.Headers.ContentLength is long length && length > MaximumManifestBytes)
            throw new InvalidDataException("Manifest exceeds the 2 MB limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (memory.Length + read > MaximumManifestBytes)
                throw new InvalidDataException("Manifest exceeds the 2 MB limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var json = Encoding.UTF8.GetString(memory.ToArray());
        var manifest = ModpackManifestValidator.ParseAndValidate(json);
        Directory.CreateDirectory(instanceDirectory);

        for (var index = 0; index < manifest.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[index];
            progress?.Report(new ModpackInstallProgress(
                manifest.DisplayName, file.Path, index, manifest.Files.Count, 0));

            var fileProgress = new Progress<double>(value =>
                progress?.Report(new ModpackInstallProgress(
                    manifest.DisplayName, file.Path, index, manifest.Files.Count, value)));

            try
            {
                await _installer.InstallFileAsync(file, instanceDirectory, fileProgress, cancellationToken);
            }
            catch when (!file.Required && !cancellationToken.IsCancellationRequested)
            {
                // Optional files may fail without blocking the required pack.
            }

            progress?.Report(new ModpackInstallProgress(
                manifest.DisplayName, file.Path, index + 1, manifest.Files.Count, 1));
        }

        return manifest;
    }
}

public sealed record ModpackInstallProgress(
    string PackName,
    string CurrentFile,
    int CompletedFiles,
    int TotalFiles,
    double CurrentFileProgress)
{
    public double OverallProgress => TotalFiles == 0
        ? 1d
        : Math.Clamp((CompletedFiles + CurrentFileProgress) / TotalFiles, 0d, 1d);
}
