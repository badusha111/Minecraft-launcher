using System.Diagnostics;
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

            var match = Regex.Match(output, @"version\s+\"(?<version>[^\"]+)\"");
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
