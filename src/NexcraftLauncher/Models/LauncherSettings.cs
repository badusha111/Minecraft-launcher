using System.Text.Json.Serialization;

namespace NexcraftLauncher.Models;

public sealed class LauncherSettings
{
    [JsonPropertyName("selectedMinecraftVersion")]
    public string SelectedMinecraftVersion { get; set; } = "1.21.1";

    [JsonPropertyName("playerNickname")]
    public string PlayerNickname { get; set; } = "";

    [JsonPropertyName("memoryMinimumMb")]
    public int MemoryMinimumMb { get; set; } = 1024;

    [JsonPropertyName("memoryMaximumMb")]
    public int MemoryMaximumMb { get; set; } = 4096;

    [JsonPropertyName("gameDirectory")]
    public string GameDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NEXCRAFT",
        "instances",
        "default");

    [JsonPropertyName("closeLauncherOnGameStart")]
    public bool CloseLauncherOnGameStart { get; set; }
}