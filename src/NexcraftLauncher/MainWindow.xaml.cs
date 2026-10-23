using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
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

        ShowSectionWindow("NEXCRAFT — Моды и сборки", content, 650, 560);
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

        ShowSectionWindow("NEXCRAFT — Библиотека", content, 600, 500);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(SettingsNavButton);
        var settingsWindow = new SettingsWindow { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            _settings = _settingsService.Load();
            StatusText.Text = "Настройки сохранены.";
        }
    }

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

    private void ShowSectionWindow(string title, UIElement content, double width, double height)
    {
        var scrollViewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 480,
            MinHeight = 380,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#151922"),
            Foreground = System.Windows.Media.Brushes.White,
            Content = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(14),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#191F2A"),
                BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#343D4D"),
                BorderThickness = new Thickness(1),
                Child = scrollViewer
            }
        };
        window.ShowDialog();
    }

    private void ShowHomeView()
    {
        StatusText.Text = "Главная открыта. Выбери версию и нажми «ИГРАТЬ».";
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
        if (string.IsNullOrWhiteSpace(label)) return;

        var parts = label.Split('—', StringSplitOptions.TrimEntries);
        if (parts.Length > 0)
        {
            _settings.SelectedMinecraftVersion = parts[0];
            try { _settingsService.Save(_settings); } catch { }
        }
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
