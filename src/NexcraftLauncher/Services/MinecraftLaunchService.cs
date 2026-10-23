using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace NexcraftLauncher.Services;

public sealed record MinecraftSession(string Username, string Uuid, string AccessToken, string UserType = "msa");

/// <summary>Builds the official Mojang launch command and starts the installed game for an authenticated account.</summary>
public sealed class MinecraftLaunchService
{
    public async Task<Process> LaunchAsync(string versionId, string gameDirectory, string javaExecutable,
        MinecraftSession session, int minMemoryMb = 1024, int maxMemoryMb = 4096,
        IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.AccessToken) || string.IsNullOrWhiteSpace(session.Uuid))
            throw new InvalidOperationException("Для запуска требуется действующая официальная сессия Minecraft.");
        if (!File.Exists(javaExecutable))
            throw new FileNotFoundException("Не найден Java runtime.", javaExecutable);

        var root = Path.GetFullPath(gameDirectory);
        var versionDir = Path.Combine(root, "versions", versionId);
        var jsonPath = Path.Combine(versionDir, versionId + ".json");
        var clientPath = Path.Combine(versionDir, versionId + ".jar");
        if (!File.Exists(jsonPath) || !File.Exists(clientPath))
            throw new InvalidDataException("Игровые файлы не установлены. Сначала нажми «Играть» для загрузки.");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath, cancellationToken));
        var metadata = doc.RootElement;
        var mainClass = GetString(metadata, "mainClass")
            ?? throw new InvalidDataException("В описании версии нет mainClass.");

        status?.Report("Подготавливаю библиотеки и нативные файлы…");
        var libraries = new List<string>();
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);
        if (metadata.TryGetProperty("libraries", out var libraryArray))
        {
            foreach (var lib in libraryArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAllowedByRules(lib)) continue;
                if (!lib.TryGetProperty("downloads", out var downloads)) continue;

                if (downloads.TryGetProperty("artifact", out var artifact) &&
                    GetString(artifact, "path") is string relativeArtifact)
                {
                    var artifactPath = SafeCombine(Path.Combine(root, "libraries"), relativeArtifact);
                    if (File.Exists(artifactPath)) libraries.Add(artifactPath);
                }

                if (downloads.TryGetProperty("classifiers", out var classifiers) &&
                    GetString(lib, "natives") is string natives &&
                    classifiers.TryGetProperty(ResolveNativeClassifier(natives), out var nativeArtifact) &&
                    GetString(nativeArtifact, "path") is string relativeNative)
                {
                    var nativePath = SafeCombine(Path.Combine(root, "libraries"), relativeNative);
                    if (File.Exists(nativePath))
                        await ExtractNativesAsync(nativePath, nativesDir, cancellationToken);
                }
            }
        }

        var assetIndex = metadata.TryGetProperty("assetIndex", out var assetIndexElement)
            ? GetString(assetIndexElement, "id") ?? versionId : versionId;
        var assetsRoot = Path.Combine(root, "assets");
        var classpath = string.Join(Path.PathSeparator, libraries.Append(clientPath));
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = session.Username,
            ["version_name"] = versionId,
            ["game_directory"] = root,
            ["assets_root"] = assetsRoot,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = session.Uuid.Replace("-", ""),
            ["auth_access_token"] = session.AccessToken,
            ["clientid"] = "",
            ["auth_xuid"] = "",
            ["user_type"] = session.UserType,
            ["version_type"] = GetString(metadata, "type") ?? "release",
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = "NEXCRAFT",
            ["launcher_version"] = "1.0.0",
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = Path.Combine(root, "libraries"),
            ["user_properties"] = "{}"
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(javaExecutable),
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        startInfo.ArgumentList.Add($"-Xms{Math.Clamp(minMemoryMb, 512, 32768)}M");
        startInfo.ArgumentList.Add($"-Xmx{Math.Clamp(Math.Max(minMemoryMb, maxMemoryMb), 1024, 65536)}M");
        startInfo.ArgumentList.Add("-Djava.library.path=" + nativesDir);

        if (metadata.TryGetProperty("arguments", out var args) &&
            args.TryGetProperty("jvm", out var jvmArgs) && jvmArgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in ExpandArguments(jvmArgs, replacements))
                startInfo.ArgumentList.Add(arg);
        }
        else
        {
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(classpath);
        }

        startInfo.ArgumentList.Add(mainClass);
        if (metadata.TryGetProperty("arguments", out args) &&
            args.TryGetProperty("game", out var gameArgs) && gameArgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in ExpandArguments(gameArgs, replacements))
                startInfo.ArgumentList.Add(arg);
        }
        else if (GetString(metadata, "minecraftArguments") is string legacy)
        {
            foreach (var arg in SplitLegacyArguments(legacy))
                startInfo.ArgumentList.Add(Replace(arg, replacements));
        }
        else
        {
            throw new InvalidDataException("Не удалось прочитать игровые аргументы этой версии.");
        }

        status?.Report("Запускаю Minecraft…");
        cancellationToken.ThrowIfCancellationRequested();
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows не смог запустить Minecraft.");
    }

    private static IEnumerable<string> ExpandArguments(JsonElement array, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                yield return Replace(element.GetString() ?? "", replacements);
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object || !IsAllowedByRules(element)) continue;
            if (!element.TryGetProperty("value", out var value)) continue;
            if (value.ValueKind == JsonValueKind.String)
                yield return Replace(value.GetString() ?? "", replacements);
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        yield return Replace(item.GetString() ?? "", replacements);
        }
    }

    private static bool IsAllowedByRules(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var matches = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (GetString(os, "name") is string name && name != "windows") matches = false;
                if (GetString(os, "arch") is string arch && arch is not ("x86" or "x86_64" or "amd64")) matches = false;
            }
            if (rule.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Object &&
                features.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.True))
                matches = false;
            if (matches)
            {
                var action = GetString(rule, "action");
                allowed = action == "allow";
            }
        }
        return allowed;
    }

    private static string ResolveNativeClassifier(string native)
    {
        var arch = Environment.Is64BitOperatingSystem ? "64" : "32";
        return native.Replace("${arch}", arch, StringComparison.Ordinal);
    }

    private static async Task ExtractNativesAsync(string archivePath, string destination, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                continue;
            var output = SafeCombine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using var input = entry.Open();
            await using var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true);
            await input.CopyToAsync(target, ct);
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Недопустимый путь внутри игрового архива.");
        return fullPath;
    }

    private static string Replace(string value, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var item in replacements)
            value = value.Replace("${" + item.Key + "}", item.Value, StringComparison.Ordinal);
        return value;
    }

    private static IEnumerable<string> SplitLegacyArguments(string arguments) =>
        System.Text.RegularExpressions.Regex.Matches(arguments, @"[^\s""']+|""[^""]*""|'[^']*'")
            .Select(m => m.Value.Trim('"', '\''));

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
