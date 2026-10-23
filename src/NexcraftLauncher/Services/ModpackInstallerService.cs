using System.Security.Cryptography;
using NexcraftLauncher.Models;

namespace NexcraftLauncher.Services;

/// <summary>
/// Downloads one approved manifest file, verifies its SHA-256 digest, then atomically
/// places it under the selected instance directory. Downloaded files are never executed.
/// </summary>
public sealed class ModpackInstallerService
{
    private const long MaximumFileBytes = 512L * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public ModpackInstallerService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task InstallFileAsync(
        ModpackFile file,
        string instanceDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceDirectory);

        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Modpack downloads must use HTTPS.");

        if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Invalid SHA-256 hash for '{file.Path}'.");

        var root = Path.GetFullPath(instanceDirectory);
        Directory.CreateDirectory(root);
        var destination = GetSafeDestination(root, file.Path);
        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Invalid destination directory.");

        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(destinationDirectory, $".nexcraft-{Guid.NewGuid():N}.download");

        try
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Modpack download redirects must remain on HTTPS.");

            if (response.Content.Headers.ContentLength is long length && length > MaximumFileBytes)
                throw new InvalidDataException("The requested modpack file exceeds the 512 MB limit.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > MaximumFileBytes)
                    throw new InvalidDataException("The requested modpack file exceeds the 512 MB limit.");

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (response.Content.Headers.ContentLength is long expected && expected > 0)
                    progress?.Report(Math.Clamp((double)total / expected, 0d, 1d));
            }

            await output.FlushAsync(cancellationToken);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(file.Sha256)))
            {
                throw new InvalidDataException($"SHA-256 verification failed for '{file.Path}'.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, overwrite: true);
            progress?.Report(1d);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best effort cleanup; the primary install error is more useful.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup; the primary install error is more useful.
            }
        }
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException("Modpack file path is empty.");

        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(relativePath) ||
            normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe modpack path: '{relativePath}'.");
        }

        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Modpack path escapes the instance directory: '{relativePath}'.");

        return destination;
    }
}