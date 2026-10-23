using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace NexcraftLauncher.Services;

public sealed record JavaRuntimeInfo(
    string ExecutablePath,
    int MajorVersion,
    string VersionText)
{
    public bool SupportsModernMinecraft => MajorVersion >= 21;
}

/// <summary>
/// Detects an installed Java runtime without downloading or executing game files.
/// Minecraft versions 1.20.5 and newer generally require Java 21; older versions
/// may use a different runtime, so callers should check the selected game version.
/// </summary>
public sealed class JavaRuntimeService
{
    private static readonly string[] CommonExecutables =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java", "bin", "java.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium", "bin", "java.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium", "bin", "java.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft", "jdk", "bin", "java.exe")
    ];

    /// <summary>
    /// Installs a matching Eclipse Temurin JRE into the launcher's game directory.
    /// This downloads Java only; it does not bypass Minecraft account authentication.
    /// </summary>
    public async Task<JavaRuntimeInfo> EnsureRuntimeAsync(
        int majorVersion,
        string gameDirectory,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (majorVersion is not (8 or 17 or 21))
            throw new ArgumentOutOfRangeException(nameof(majorVersion), "Supported Minecraft Java runtimes are 8, 17 and 21.");

        var runtimeRoot = Path.GetFullPath(Path.Combine(gameDirectory, "runtime", $"java-{majorVersion}"));
        var existing = await TryFindRuntimeAsync(runtimeRoot, majorVersion, cancellationToken);
        if (existing is not null)
            return existing;

        status?.Report($"Скачиваю Java {majorVersion} для Minecraft…");
        var downloadUri = new Uri($"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/windows/x64/jre/hotspot/normal/eclipse");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Загрузка Java должна оставаться на HTTPS.");
        if (response.Content.Headers.ContentLength is long length && length > 500L * 1024 * 1024)
            throw new InvalidDataException("Архив Java превышает допустимый размер 500 МБ.");

        Directory.CreateDirectory(Path.GetDirectoryName(runtimeRoot)!);
        var zipPath = runtimeRoot + "." + Guid.NewGuid().ToString("N") + ".zip";
        var extractPath = runtimeRoot + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > 500L * 1024 * 1024)
                        throw new InvalidDataException("Архив Java превышает допустимый размер 500 МБ.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            status?.Report($"Распаковываю Java {majorVersion}…");
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
            var javaExe = Directory.GetFiles(extractPath, "java.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase));
            if (javaExe is null)
                throw new InvalidDataException("В архиве Java не найден исполняемый файл bin\\java.exe.");

            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, recursive: true);
            Directory.Move(extractPath, runtimeRoot);
            var relativeJava = Path.GetRelativePath(extractPath, javaExe);
            var installedJava = Path.Combine(runtimeRoot, relativeJava);
            var runtime = await TryReadVersionAsync(installedJava, cancellationToken);
            if (runtime is null || runtime.MajorVersion != majorVersion)
                throw new InvalidDataException($"Не удалось подтвердить установленную Java {majorVersion}.");
            status?.Report($"Java {majorVersion} готова.");
            return runtime;
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (IOException) { }
            try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, recursive: true); } catch (IOException) { }
        }
    }

    private async Task<JavaRuntimeInfo?> TryFindRuntimeAsync(string runtimeRoot, int expectedMajor, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(runtimeRoot)) return null;
        foreach (var javaExe in Directory.GetFiles(runtimeRoot, "java.exe", SearchOption.AllDirectories)
                     .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase)))
        {
            var runtime = await TryReadVersionAsync(javaExe, cancellationToken);
            if (runtime?.MajorVersion == expectedMajor) return runtime;
        }
        return null;
    }

    public async Task<JavaRuntimeInfo?> DetectAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(candidate))
                candidates.Add(candidate);
        }

        candidates.AddRange(CommonExecutables.Where(File.Exists));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = await TryReadVersionAsync(candidate, cancellationToken);
            if (runtime is not null)
                return runtime;
        }

        return null;
    }

    public async Task<JavaRuntimeInfo?> TryReadVersionAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
                return null;

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });

            await process.WaitForExitAsync(cancellationToken);
            var output = (await stderrTask) + Environment.NewLine + (await stdoutTask);
            if (process.ExitCode != 0)
                return null;

            var match = Regex.Match(output, @"version\s+""(?<version>[^""]+)""");
            if (!match.Success)
                return null;

            var version = match.Groups["version"].Value;
            var majorMatch = Regex.Match(version, @"^(?:1\.)?(?<major>\d+)");
            if (!majorMatch.Success || !int.TryParse(majorMatch.Groups["major"].Value, out var major))
                return null;

            // Legacy Java versions such as 1.8 report major 8.
            return new JavaRuntimeInfo(Path.GetFullPath(executablePath), major, version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
