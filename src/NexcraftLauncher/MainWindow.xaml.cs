using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using NexcraftLauncher.Models;
using NexcraftLauncher.Services;

// Подключаем движок лаунчера
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace NexcraftLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherSettingsService _settingsService = new();
    private readonly MinecraftMetadataService _metadataService = new();
    private LauncherSettings _settings;
    private bool _isInstalling;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        
        PlayerNicknameTextBox.Text = _settings.PlayerNickname;
        RestoreSelectedVersion();
        foreach (ComboBoxItem item in LoaderSelector.Items)
            if (string.Equals(item.Content?.ToString(), _settings.SelectedMinecraftLoader, StringComparison.OrdinalIgnoreCase))
                LoaderSelector.SelectedItem = item;
        LoaderSelector.SelectionChanged += (_, _) => SaveSelectedVersion();

        StatusText.Text = "Введи свой ник, выбери версию игры и нажми играть.";
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            VersionInfoText.Text = "Загружаем официальный список версий...";
            var manifest = await _metadataService.GetVersionManifestAsync();
            var releases = manifest.Versions
                .Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (releases.Count == 0)
                throw new InvalidDataException("В официальном списке нет стабильных релизов.");

            VersionSelector.Items.Clear();
            foreach (var version in releases)
            {
                VersionSelector.Items.Add(new ComboBoxItem
                {
                    Content = $"{version.Id} — Vanilla"
                });
            }

            RestoreSelectedVersion();
            VersionInfoText.Text = $"Доступно стабильных версий: {releases.Count} · официальный каталог Mojang";
            StatusText.Text = "Список обновлён. Выбери версию и нажми «ИГРАТЬ».";
        }
        catch
        {
            // Keep the built-in versions available if the official metadata service is temporarily unreachable.
            VersionInfoText.Text = $"Не удалось обновить список. Резервных версий: {VersionSelector.Items.Count}.";
            RestoreSelectedVersion();
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(HomeNavButton);
        HomeScrollViewer.Visibility = Visibility.Visible;
        SectionScrollViewer.Visibility = Visibility.Collapsed;
        SectionContent.Children.Clear();
        ShowHomeView();
    }

    private void Mods_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(ModsNavButton);
        var root = GetGameDirectory();
        var modsDirectory = Path.Combine(root, "mods");
        var files = Directory.Exists(modsDirectory)
            ? Directory.GetFiles(modsDirectory, "*.jar", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = "МОДЫ И СБОРКИ",
            Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#B99AFF"),
            FontSize = 11,
            FontWeight = FontWeights.Bold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Моды установленной игры",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Папка: {modsDirectory}",
            Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#AEB8C8"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        if (files.Length == 0)
        {
            content.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#202631"),
                BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#51427D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                Child = new TextBlock
                {
                    Text = "вы пока что не установили ни одного мода",
                    FontSize = 16,
                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E0D8FF"),
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Найдено модов: {files.Length}",
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#19F58A"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            foreach (var file in files)
            {
                var card = new Border
                {
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#202631"),
                    BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#343D4D"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = new StackPanel()
                };
                ((StackPanel)card.Child).Children.Add(new TextBlock
                {
                    Text = Path.GetFileNameWithoutExtension(file),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.White
                });
                ((StackPanel)card.Child).Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(file),
                    FontSize = 11,
                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#AEB8C8"),
                    Margin = new Thickness(0, 3, 0, 0)
                });
                content.Children.Add(card);
            }
        }

        ShowSectionPage(content);
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(LibraryNavButton);
        var versionsDirectory = Path.Combine(GetGameDirectory(), "versions");
        var versions = Directory.Exists(versionsDirectory)
            ? Directory.GetDirectories(versionsDirectory)
                .Where(path => File.Exists(Path.Combine(path, Path.GetFileName(path) + ".json")))
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = "БИБЛИОТЕКА",
            Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#B99AFF"),
            FontSize = 11,
            FontWeight = FontWeights.Bold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Установленные версии",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 18)
        });

        if (versions.Length == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Пока нет установленных версий. Нажми «ИГРАТЬ», чтобы установить выбранную версию.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#D0D7E2")
            });
        }
        else
        {
            foreach (var versionPath in versions)
            {
                content.Children.Add(new Border
                {
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#202631"),
                    BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#343D4D"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock
                    {
                        Text = Path.GetFileName(versionPath),
                        FontSize = 15,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = System.Windows.Media.Brushes.White
                    }
                });
            }
        }

        ShowSectionPage(content);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(SettingsNavButton);
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "НАСТРОЙКИ ЛАУНЧЕРА", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#B99AFF") });
        content.Children.Add(new TextBlock { Text = "Подстрой игру под свой компьютер", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 18) });
        content.Children.Add(new TextBlock { Text = "Папка игры", Margin = new Thickness(0, 0, 0, 6) });
        var folder = new TextBox { Text = GetGameDirectory(), Height = 38, Padding = new Thickness(9, 7, 9, 7) };
        content.Children.Add(folder);
        var browse = new Button { Content = "Обзор…", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 8, 0, 16), HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку для файлов Minecraft", Multiselect = false, InitialDirectory = Directory.Exists(folder.Text) ? folder.Text : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) };
            if (dialog.ShowDialog() == true) folder.Text = dialog.FolderName;
        };
        content.Children.Add(browse);
        content.Children.Add(new TextBlock { Text = "Память для Minecraft (МБ)", Margin = new Thickness(0, 0, 0, 6) });
        var memory = new Grid();
        memory.ColumnDefinitions.Add(new ColumnDefinition());
        memory.ColumnDefinitions.Add(new ColumnDefinition());
        var min = MakeMemoryCombo(new[] { 512, 1024, 2048, 4096 }, _settings.MemoryMinimumMb);
        var max = MakeMemoryCombo(new[] { 2048, 4096, 6144, 8192, 12288, 16384 }, _settings.MemoryMaximumMb);
        Grid.SetColumn(max, 1);
        memory.Children.Add(min);
        memory.Children.Add(max);
        content.Children.Add(memory);
        var close = new CheckBox { Content = "Закрывать лаунчер после запуска игры", IsChecked = _settings.CloseLauncherOnGameStart, Margin = new Thickness(0, 16, 0, 0) };
        content.Children.Add(close);
        var error = new TextBlock { Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFB36B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        content.Children.Add(error);
        var save = new Button { Content = "Сохранить настройки", Padding = new Thickness(18, 10, 18, 10), Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#19F58A"), Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#092318"), FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0) };
        save.Click += (_, _) =>
        {
            int minValue = MemoryValue(min, 1024);
            int maxValue = MemoryValue(max, 4096);
            if (string.IsNullOrWhiteSpace(folder.Text)) { error.Text = "Укажите папку игры."; return; }
            if (maxValue < minValue) { error.Text = "Максимальная память должна быть не меньше минимальной."; return; }
            try
            {
                _settings.GameDirectory = folder.Text.Trim();
                _settings.MemoryMinimumMb = minValue;
                _settings.MemoryMaximumMb = maxValue;
                _settings.CloseLauncherOnGameStart = close.IsChecked == true;
                _settingsService.Save(_settings);
                StatusText.Text = "Настройки сохранены.";
                error.Text = "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error.Text = $"Не удалось сохранить настройки: {ex.Message}"; }
        };
        content.Children.Add(save);
        ShowSectionPage(content);
    }

    private static ComboBox MakeMemoryCombo(int[] values, int selected)
    {
        var combo = new ComboBox { Height = 38, Margin = new Thickness(4, 0, 4, 0) };
        foreach (var value in values) combo.Items.Add(new ComboBoxItem { Content = value.ToString() });
        foreach (ComboBoxItem item in combo.Items)
            if (int.TryParse(item.Content?.ToString(), out var parsed) && parsed == selected) { combo.SelectedItem = item; return combo; }
        combo.SelectedIndex = 0;
        return combo;
    }

    private static int MemoryValue(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var value) ? value : fallback;

    private string GetGameDirectory() =>
        string.IsNullOrWhiteSpace(_settings.GameDirectory)
            ? MinecraftPath.GetOSDefaultPath()
            : _settings.GameDirectory;

    private void SetActiveNavigation(Button activeButton)
    {
        var normal = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#C7CEDA");
        var transparent = System.Windows.Media.Brushes.Transparent;
        foreach (var button in new[] { HomeNavButton, ModsNavButton, LibraryNavButton, SettingsNavButton })
        {
            button.Background = transparent;
            button.Foreground = normal;
            button.BorderBrush = transparent;
        }

        activeButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#25213A");
        activeButton.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#B99AFF");
        activeButton.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#51427D");
    }

    private void ShowSectionPage(UIElement content)
    {
        HomeScrollViewer.Visibility = Visibility.Collapsed;
        SectionContent.Children.Clear();
        SectionContent.Children.Add(content);
        SectionScrollViewer.Visibility = Visibility.Visible;
    }

    private void ShowHomeView()
    {
        StatusText.Text = "Главная открыта. Выбери версию и нажми «ИГРАТЬ».";
    }


    private static int RequiredJavaMajor(string gameVersion)
    {
        var parts = gameVersion.Split('.');
        if (parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var minor))
        {
            if (minor >= 21) return 21;
            if (minor == 20)
            {
                var patch = parts.Length >= 3 && int.TryParse(parts[2].Split('-')[0], out var p) ? p : 0;
                return patch >= 5 ? 21 : 17;
            }
            if (minor >= 18) return 17;
            if (minor == 17) return 16;
        }
        return 8;
    }

    private static string FindJavaExecutable(string gameVersion, string gameDir)
    {
        int requiredMajor = RequiredJavaMajor(gameVersion);
        var candidates = new System.Collections.Generic.List<string>();
        void AddIfExists(string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate) &&
                !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                candidates.Add(candidate);
        }

        string javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var runtimeRoots = new[]
        {
            Path.Combine(gameDir, "runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Java",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files\Zulu",
            @"C:\Program Files\BellSoft"
        };

        foreach (var root in runtimeRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, javaName, SearchOption.AllDirectories).Take(150))
                    if (Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals("bin", StringComparison.OrdinalIgnoreCase))
                        AddIfExists(file);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            AddIfExists(Path.Combine(javaHome, "bin", javaName));

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            AddIfExists(Path.Combine(folder.Trim('"'), javaName));

        foreach (var candidate in candidates)
        {
            try
            {
                var start = new ProcessStartInfo(candidate, "-version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(start);
                if (process is null) continue;
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                var output = outputTask.GetAwaiter().GetResult() + errorTask.GetAwaiter().GetResult();
                var match = System.Text.RegularExpressions.Regex.Match(output, @"version\s+\"(?:1\.)?(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var major) && major == requiredMajor)
                    return candidate;
            }
            catch { }
        }

        string requirement = requiredMajor switch
        {
            21 => "Java 21",
            17 => "Java 17",
            16 => "Java 16",
            _ => "Java 8"
        };
        throw new InvalidOperationException(
            $"Для Minecraft {gameVersion} нужен {requirement}, но подходящая Java не найдена. Сейчас у тебя обнаружена Java 8 (1.8.0_401). Установи {requirement} и повтори запуск. Лаунчер больше не будет запускать установщик Forge заведомо неподходящей Java.");
    }

    private async Task<string> PrepareLoaderVersionAsync(string loaderLabel, string gameVersion, string gameDir, MinecraftPath path)
    {
        if (loaderLabel.StartsWith("Vanilla", StringComparison.OrdinalIgnoreCase))
            return gameVersion;

        if (loaderLabel.StartsWith("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            using var http = new HttpClient();
            var profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/profile/json";
            using var response = await http.GetAsync(profileUrl);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var profileName = document.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(profileName))
                throw new InvalidDataException("Fabric не вернул имя профиля.");
            var jsonPath = path.GetVersionJsonPath(profileName);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            await File.WriteAllTextAsync(jsonPath, json);
            return profileName;
        }

        if (loaderLabel.StartsWith("Forge", StringComparison.OrdinalIgnoreCase))
        {
            using var http = new HttpClient();
            using var promotionsResponse = await http.GetAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
            promotionsResponse.EnsureSuccessStatusCode();
            using var promotions = JsonDocument.Parse(await promotionsResponse.Content.ReadAsStringAsync());
            var promos = promotions.RootElement.GetProperty("promos");
            if (!promos.TryGetProperty(gameVersion + "-recommended", out var forgeVersion) &&
                !promos.TryGetProperty(gameVersion + "-latest", out forgeVersion))
                throw new InvalidOperationException($"Для Minecraft {gameVersion} не найден подходящий Forge.");
            var forgeBuild = forgeVersion.GetString();
            if (string.IsNullOrWhiteSpace(forgeBuild)) throw new InvalidDataException("Не удалось определить версию Forge.");
            var fullVersion = $"{gameVersion}-{forgeBuild}";
            var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{fullVersion}/forge-{fullVersion}-installer.jar";
            var installerPath = Path.Combine(Path.GetTempPath(), $"forge-{fullVersion}-installer.jar");
            await File.WriteAllBytesAsync(installerPath, await http.GetByteArrayAsync(installerUrl));
            var start = new ProcessStartInfo(FindJavaExecutable(gameVersion, gameDir)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-jar");
            start.ArgumentList.Add(installerPath);
            start.ArgumentList.Add("--installClient");
            start.ArgumentList.Add(gameDir);
            using var installer = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить Java для Forge. Установи подходящую Java.");
            var stdoutTask = installer.StandardOutput.ReadToEndAsync();
            var stderrTask = installer.StandardError.ReadToEndAsync();
            await installer.WaitForExitAsync();
            var installerOutput = (await stdoutTask) + Environment.NewLine + (await stderrTask);
            if (installer.ExitCode != 0) throw new InvalidOperationException($"Установщик Forge завершился с кодом {installer.ExitCode}: {installerOutput}");
            var versionsDir = Path.Combine(gameDir, "versions");
            var installed = Directory.Exists(versionsDir)
                ? Directory.GetDirectories(versionsDir)
                    .Where(dir => Path.GetFileName(dir).StartsWith(gameVersion + "-forge-", StringComparison.OrdinalIgnoreCase)
                               || Path.GetFileName(dir).StartsWith(gameVersion + "-Forge", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            if (installed is null) throw new DirectoryNotFoundException($"Forge сообщил об успешной установке, но профиль не найден в {versionsDir}. Вывод установщика: {installerOutput}");
            return Path.GetFileName(installed);
        }

        if (loaderLabel.StartsWith("OptiFine", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = new OpenFileDialog { Title = "Выбери установщик OptiFine для выбранной версии Minecraft", Filter = "Java installer (*.jar)|*.jar", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) throw new OperationCanceledException("Установка OptiFine отменена.");
            var start = new ProcessStartInfo(FindJavaExecutable(gameVersion, gameDir)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-jar"); start.ArgumentList.Add(dialog.FileName); start.ArgumentList.Add("-install");
            using var installer = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить Java для OptiFine. Установи подходящую Java.");
            var stdoutTask = installer.StandardOutput.ReadToEndAsync();
            var stderrTask = installer.StandardError.ReadToEndAsync();
            await installer.WaitForExitAsync();
            if (installer.ExitCode != 0) throw new InvalidOperationException($"Установщик OptiFine завершился с кодом {installer.ExitCode}: {(await stdoutTask)}{Environment.NewLine}{(await stderrTask)}");
            var versionsDir = Path.Combine(gameDir, "versions");
            var installed = Directory.Exists(versionsDir) ? Directory.GetDirectories(versionsDir, gameVersion + "-OptiFine*").OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (installed is null) throw new DirectoryNotFoundException("Профиль OptiFine не найден. Проверь, что установщик подходит к выбранной версии.");
            return Path.GetFileName(installed);
        }

        throw new NotSupportedException("Неизвестный загрузчик Minecraft.");
    }

    private void RestoreSelectedVersion()
    {
        foreach (ComboBoxItem item in VersionSelector.Items)
        {
            var label = item.Content?.ToString() ?? string.Empty;
            if (label.StartsWith(_settings.SelectedMinecraftVersion, StringComparison.OrdinalIgnoreCase))
            {
                VersionSelector.SelectedItem = item;
                return;
            }
        }
        VersionSelector.SelectedIndex = 0;
    }

    private void SaveSelectedVersion()
    {
        var label = (VersionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(label))
        {
            var parts = label.Split('—', StringSplitOptions.TrimEntries);
            if (parts.Length > 0) _settings.SelectedMinecraftVersion = parts[0];
        }
        _settings.SelectedMinecraftLoader = (LoaderSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla — обычная игра";
        try { _settingsService.Save(_settings); } catch { }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        var nickname = PlayerNicknameTextBox.Text.Trim();
        
        if (!System.Text.RegularExpressions.Regex.IsMatch(nickname, @"^[A-Za-z0-9_]{3,16}$"))
        {
            StatusText.Text = "Введи ник длиной 3–16 символов (латиница, цифры).";
            PlayerNicknameTextBox.Focus();
            return;
        }

        _settings.PlayerNickname = nickname;
        try { _settingsService.Save(_settings); } catch { }

        SaveSelectedVersion();
        
        var label = (VersionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(label))
        {
            StatusText.Text = "Сначала выбери версию Minecraft.";
            return;
        }
        
        var parts = label.Split('—', StringSplitOptions.TrimEntries);
        string version = parts[0];

        _isInstalling = true;
        PlayButton.IsEnabled = false;

        try
        {
            string gameDir = string.IsNullOrWhiteSpace(_settings.GameDirectory) 
                ? MinecraftPath.GetOSDefaultPath() 
                : _settings.GameDirectory;

            var path = new MinecraftPath(gameDir);
            var launcher = new MinecraftLauncher(path);

            launcher.FileProgressChanged += (pSender, pArgs) =>
            {
                Dispatcher.Invoke(() =>
                {
                    int percentage = pArgs.TotalTasks > 0 ? (int)((double)pArgs.ProgressedTasks / pArgs.TotalTasks * 100) : 0;
                    StatusText.Text = $"Проверка файлов: {pArgs.ProgressedTasks}/{pArgs.TotalTasks} ({percentage}%)";
                });
            };

            StatusText.Text = "Создание автономного профиля...";
            
            // Рассчитываем правильный оффлайн-UUID по алгоритму Java-серверов
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + nickname);
            byte[] hashBytes = System.Security.Cryptography.MD5.HashData(nameBytes);
            hashBytes[6] = (byte)((hashBytes[6] & 0x0f) | 0x30);
            hashBytes[8] = (byte)((hashBytes[8] & 0x3f) | 0x80);
            string offlineUuid = new Guid(hashBytes).ToString("N");
            
            // Создаем чистую оффлайн-сессию
            var session = new MSession(nickname, "00000000000000000000000000000000", offlineUuid);
            session.UserType = "legacy";

            var launchOption = new MLaunchOption
            {
                MaximumRamMb = 4096,
                MinimumRamMb = 1024,
                Session = session
            };

            // RulesContext отсутствует в используемой версии CmlLib.
            // Сохраняем штатные параметры запуска; доступ к сетевой игре зависит от версии клиента
            // и настроек сервера, а не от этого неподдерживаемого свойства.

            StatusText.Text = "Анализ файлов и загрузка компонентов...";
            
            string selectedLoader = (LoaderSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";
            StatusText.Text = $"Подготавливаем базовую версию Minecraft {version}...";
            await launcher.InstallAsync(version);
            version = await PrepareLoaderVersionAsync(selectedLoader, version, gameDir, path);
            StatusText.Text = $"Подготавливаем {selectedLoader.Split('—')[0].Trim()} для Minecraft {version}...";
            await launcher.InstallAsync(version);
            Process process = await launcher.InstallAndBuildProcessAsync(version, launchOption);
            
            StatusText.Text = "Запуск игрового клиента...";
            process.Start();

            StatusText.Text = "Игра успешно запущена!";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка запуска: {ex.Message}";
        }
        finally
        {
            _isInstalling = false;
            PlayButton.IsEnabled = true;
        }
    }
}
