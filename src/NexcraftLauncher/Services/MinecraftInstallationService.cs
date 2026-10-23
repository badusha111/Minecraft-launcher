using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexcraftLauncher.Services;

/// <summary>Downloads and verifies the official Minecraft version JSON, client, libraries and assets.</summary>
public sealed class MinecraftInstallationService
{
    private const long MaxFileBytes = 1024L * 1024 * 1024;
    private const long MaxMetadataBytes = 16L * 1024 * 1024;
    private readonly HttpClient _http;

    public MinecraftInstallationService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task InstallAsync(string versionId, string gameDirectory, IProgress<string>? status = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId) ||
            versionId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new InvalidDataException("Некорректный идентификатор версии.");

        var root = Path.GetFullPath(gameDirectory);
        Directory.CreateDirectory(root);
        var metadataService = new MinecraftMetadataService(_http);
        status?.Report("Получаю официальный список версий…");
        var metadata = await metadataService.GetVersionMetadataAsync(versionId, cancellationToken);
        var manifest = await DownloadJsonAsync(new Uri("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
            8 * 1024 * 1024, cancellationToken);
        var versionUrl = FindVersionUrl(manifest, versionId);
        status?.Report("Загружаю описание версии…");
        var versionJson = await DownloadBytesAsync(new Uri(versionUrl), MaxMetadataBytes, cancellationToken);
        using var versionDoc = JsonDocument.Parse(versionJson);
        var versionDir = Path.Combine(root, "versions", versionId);
        Directory.CreateDirectory(versionDir);
        await WriteAtomicAsync(Path.Combine(versionDir, versionId + ".json"), versionJson, cancellationToken);

        var client = metadata.Downloads["client"];
        status?.Report("Загружаю игровой клиент…");
        await DownloadVerifiedAsync(new Uri(client.Url), Path.Combine(versionDir, versionId + ".jar"),
            client.Sha1, client.Size, cancellationToken, p => progress?.Report(p * 0.20));

        var docRoot = versionDoc.RootElement;
        var libraries = new List<(string Url, string Path, string? Sha1, long? Size)>();
        if (docRoot.TryGetProperty("libraries", out var libraryArray) && libraryArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var library in libraryArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (library.TryGetProperty("downloads", out var downloads) &&
                    downloads.TryGetProperty("artifact", out var artifact) &&
                    artifact.TryGetProperty("url", out var urlEl) && urlEl.GetString() is string url &&
                    artifact.TryGetProperty("path", out var pathEl) && pathEl.GetString() is string relPath)
                {
                    libraries.Add((url, SafeCombine(root, Path.Combine("libraries", relPath)),
                        GetString(artifact, "sha1"), GetLong(artifact, "size")));
                }

                if (library.TryGetProperty("downloads", out downloads) &&
                    downloads.TryGetProperty("classifiers", out var classifiers) &&
                    classifiers.ValueKind == JsonValueKind.Object)
                {
                    foreach (var classifier in classifiers.EnumerateObject())
                    {
                        if (!classifier.Name.Contains("natives", StringComparison.OrdinalIgnoreCase)) continue;
                        var classifierArtifact = classifier.Value;
                        var url = GetString(classifierArtifact, "url");
                        var relPath = GetString(classifierArtifact, "path");
                        if (url is not null && relPath is not null)
                            libraries.Add((url, SafeCombine(root, Path.Combine("libraries", relPath)),
                                GetString(classifierArtifact, "sha1"), GetLong(classifierArtifact, "size")));
                    }
                }
            }
        }

        var total = Math.Max(1, libraries.Count);
        for (var i = 0; i < libraries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = libraries[i];
            status?.Report($"Загружаю библиотеки: {i + 1} из {libraries.Count}…");
            await DownloadVerifiedAsync(new Uri(item.Url), item.Path, item.Sha1, item.Size,
                cancellationToken, _ => progress?.Report(0.20 + 0.30 * (i + 1d) / total));
        }

        if (docRoot.TryGetProperty("assetIndex", out var assetIndex))
        {
            var indexId = GetString(assetIndex, "id") ?? versionId;
            var indexUrl = GetString(assetIndex, "url");
            if (indexUrl is not null)
            {
                status?.Report("Загружаю индекс ресурсов…");
                var indexBytes = await DownloadBytesAsync(new Uri(indexUrl), MaxMetadataBytes, cancellationToken);
                var indexPath = Path.Combine(root, "assets", "indexes", indexId + ".json");
                await WriteAtomicAsync(indexPath, indexBytes, cancellationToken);
                using var indexDoc = JsonDocument.Parse(indexBytes);
                if (indexDoc.RootElement.TryGetProperty("objects", out var objects) &&
                    objects.ValueKind == JsonValueKind.Object)
                {
                    var entries = objects.EnumerateObject().Select(x => x.Value)
                        .Select(v => (Hash: GetString(v, "hash"), Size: GetLong(v, "size")))
                        .Where(x => x.Hash is { Length: 40 } && x.Hash.All(Uri.IsHexDigit)).ToArray();
                    for (var i = 0; i < entries.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var item = entries[i];
                        var prefix = item.Hash![..2];
                        var assetUrl = $"https://resources.download.minecraft.net/{prefix}/{item.Hash}";
                        var assetPath = Path.Combine(root, "assets", "objects", prefix, item.Hash);
                        if (File.Exists(assetPath) && await HasSha1Async(assetPath, item.Hash, cancellationToken))
                        {
                            progress?.Report(0.50 + 0.50 * (i + 1d) / Math.Max(1, entries.Length));
                            continue;
                        }
                        status?.Report($"Загружаю ресурсы игры: {i + 1} из {entries.Length}…");
                        await DownloadVerifiedAsync(new Uri(assetUrl), assetPath, item.Hash, item.Size,
                            cancellationToken, _ => progress?.Report(0.50 + 0.50 * (i + 1d) / Math.Max(1, entries.Length)));
                    }
                }
            }
        }

        status?.Report("Проверяю установленные файлы…");
        progress?.Report(1);
        status?.Report($"Файлы Minecraft {versionId} загружены и проверены. Для запуска потребуется авторизация Microsoft.");
    }

    private async Task<JsonDocument> DownloadJsonAsync(Uri uri, long max, CancellationToken ct)
    {
        var bytes = await DownloadBytesAsync(uri, max, ct);
        return JsonDocument.Parse(bytes);
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, long max, CancellationToken ct)
    {
        EnsureHttps(uri);
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        EnsureHttps(response.RequestMessage?.RequestUri);
        if (response.Content.Headers.ContentLength is long length && length > max)
            throw new InvalidDataException("Сервер вернул файл больше допустимого размера.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (memory.Length + read > max) throw new InvalidDataException("Файл превысил допустимый размер.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return memory.ToArray();
    }

    private async Task DownloadVerifiedAsync(Uri uri, string destination, string? sha1, long? expectedSize,
        CancellationToken ct, Action<double>? progress)
    {
        EnsureHttps(uri);
        if (File.Exists(destination) && (sha1 is null || await HasSha1Async(destination, sha1, ct)) &&
            (expectedSize is null || new FileInfo(destination).Length == expectedSize))
        {
            progress?.Invoke(1);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            EnsureHttps(response.RequestMessage?.RequestUri);
            if (response.Content.Headers.ContentLength is long length && length > MaxFileBytes)
                throw new InvalidDataException("Загружаемый файл превышает лимит 1 ГБ.");
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[65536];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                total += read;
                if (total > MaxFileBytes) throw new InvalidDataException("Загружаемый файл превышает лимит 1 ГБ.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                if (response.Content.Headers.ContentLength is long lengthExpected && lengthExpected > 0)
                    progress?.Invoke(Math.Clamp((double)total / lengthExpected, 0, 1));
            }
            await output.FlushAsync(ct);
            if (expectedSize is long size && total != size)
                throw new InvalidDataException($"Размер файла не совпал с официальными данными: {destination}");
            if (sha1 is not null && !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Проверка SHA-1 не пройдена: {destination}");
            ct.ThrowIfCancellationRequested();
            File.Move(temp, destination, true);
            progress?.Invoke(1);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static async Task<bool> HasSha1Async(string path, string expected, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var actual = await SHA1.HashDataAsync(stream, ct);
        return string.Equals(Convert.ToHexString(actual), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    private static string FindVersionUrl(JsonDocument manifest, string versionId)
    {
        foreach (var item in manifest.RootElement.GetProperty("versions").EnumerateArray())
            if (GetString(item, "id") == versionId)
                return GetString(item, "url") ?? throw new InvalidDataException("В манифесте отсутствует URL версии.");
        throw new InvalidDataException("Версия не найдена в официальном манифесте.");
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static long? GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result : null;

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Путь игрового файла выходит за пределы папки игры.");
        return fullPath;
    }

    private static void EnsureHttps(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Загрузка игровых файлов разрешена только по HTTPS.");
    }
}
