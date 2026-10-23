using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace NexcraftLauncherMac;

public sealed class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _gameDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");
    private readonly string _settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXCRAFT", "settings.json");
    private readonly StackPanel _mainContent = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { Text = "NEXCRAFT для macOS · готов к настройке", Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _versions = new() { MinWidth = 210 };
    private readonly TextBox _nickname = new() { Watermark = "Ник игрока", MaxLength = 16 };
    private readonly Button _play = new() { Content = "Играть", HorizontalContentAlignment = HorizontalAlignment.Center };

    public MainWindow()
    {
        Title = "NEXCRAFT";
        Width = 1080;
        Height = 700;
        MinWidth = 820;
        MinHeight = 560;
        Background = Brush("#101319");
        Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        root.Children.Add(BuildSidebar());
        var right = new Border
        {
            Background = Brush("#151922"),
            Padding = new Thickness(28),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = "NEXCRAFT", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#B99AFF") },
                    new TextBlock { Text = "Твоя игровая библиотека", FontSize = 27, FontWeight = FontWeight.Bold },
                    _mainContent,
                    new Border { Height = 1, Background = Brush("#343D4D"), Margin = new Thickness(0, 8) },
                    _status
                }
            }
        };
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;
        LoadSettings();
        _nickname.TextChanged += (_, _) => SaveSettings();
        ShowHome();
        Opened += async (_, _) => await LoadVersionsAsync();
    }

    private Control BuildSidebar()
    {
        var menu = new StackPanel { Spacing = 8, Margin = new Thickness(16, 24) };
        menu.Children.Add(new TextBlock { Text = "NEXCRAFT", FontSize = 23, FontWeight = FontWeight.Bold, Foreground = Brush("#B99AFF"), Margin = new Thickness(8, 0, 0, 22) });
        menu.Children.Add(NavButton("⌂   Главная", ShowHome));
        menu.Children.Add(NavButton("✦   Моды и сборки", ShowMods));
        menu.Children.Add(NavButton("▦   Библиотека", ShowLibrary));
        menu.Children.Add(NavButton("⚙   Настройки", ShowSettings));
        return new Border { Background = Brush("#191E27"), Child = menu };
    }

    private Button NavButton(string title, Action action)
    {
        var button = new Button
        {
            Content = title,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brush("#202631"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#343D4D"),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void PreparePage(string title, string subtitle)
    {
        _mainContent.Children.Clear();
        _mainContent.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        _mainContent.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap });
    }

    private void ShowHome()
    {
        PreparePage("Главная", "Выбери версию, введи ник и запускай игру.");
        _mainContent.Children.Add(new TextBlock { Text = "Имя игрока", Foreground = Brush("#B8C1D0") });
        _mainContent.Children.Add(_nickname);
        _mainContent.Children.Add(new TextBlock { Text = "Версия Minecraft", Foreground = Brush("#B8C1D0"), Margin = new Thickness(0, 8, 0, 0) });
        _mainContent.Children.Add(_versions);
        _play.Background = Brush("#7C4DFF");
        _play.Foreground = Brushes.White;
        _play.Padding = new Thickness(18, 12);
        _play.Click -= PlayNotReady;
        _play.Click += PlayNotReady;
        _mainContent.Children.Add(_play);
        _status.Text = "Выбери версию. Для запуска потребуется официальная авторизация Minecraft.";
    }

    private void PlayNotReady(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveSettings();
        var nickname = _nickname.Text?.Trim() ?? string.Empty;
        if (nickname.Length == 0)
        {
            _status.Text = "Сначала введи имя игрока.";
            return;
        }
        _status.Text = "Интерфейс готов. Для запуска выбранной версии нужно подключить официальный вход Microsoft, установку Java и проверку игровых файлов.";
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsFile));
            if (document.RootElement.TryGetProperty("nickname", out var nickname))
                _nickname.Text = nickname.GetString() ?? string.Empty;
        }
        catch
        {
            // A damaged settings file must not prevent the launcher from opening.
        }
    }

    private void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFile)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new { nickname = _nickname.Text?.Trim() ?? string.Empty }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are optional; UI remains usable if the file cannot be written.
        }
    }

    private void OpenGameFolder()
    {
        try
        {
            Directory.CreateDirectory(_gameDirectory);
            Process.Start(new ProcessStartInfo { FileName = "open", ArgumentList = { _gameDirectory }, UseShellExecute = false });
            _status.Text = "Открыта папка Minecraft в Finder.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Не удалось открыть папку игры: {ex.Message}";
        }
    }

    private void ShowMods()
    {
        PreparePage("Моды и сборки", "Файлы .jar в папке mods текущей игровой директории.");
        var dir = Path.Combine(_gameDirectory, "mods");
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.jar").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        if (files.Length == 0)
            _mainContent.Children.Add(new TextBlock { Text = "вы пока что не установили ни одного мода", Foreground = Brush("#E0D8FF"), FontSize = 16, Margin = new Thickness(0, 8) });
        else
            foreach (var file in files)
                _mainContent.Children.Add(Card(Path.GetFileNameWithoutExtension(file), Path.GetFileName(file)));
        _status.Text = $"Папка модов: {dir}";
    }

    private void ShowLibrary()
    {
        PreparePage("Библиотека", "Версии, найденные в папке Minecraft.");
        var dir = Path.Combine(_gameDirectory, "versions");
        var versions = Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Where(p => File.Exists(Path.Combine(p, Path.GetFileName(p) + ".json"))).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        if (versions.Length == 0)
            _mainContent.Children.Add(new TextBlock { Text = "Пока нет установленных версий Minecraft." });
        else
            foreach (var version in versions)
                _mainContent.Children.Add(Card(Path.GetFileName(version), "Установленная версия"));
        _status.Text = $"Папка игры: {_gameDirectory}";
    }

    private void ShowSettings()
    {
        PreparePage("Настройки", "Основные параметры приложения.");
        _mainContent.Children.Add(new TextBlock { Text = "Папка игры", Foreground = Brush("#B8C1D0") });
        _mainContent.Children.Add(new TextBlock { Text = _gameDirectory, TextWrapping = TextWrapping.Wrap });
        var openFolder = NavButton("Открыть папку игры в Finder", OpenGameFolder);
        openFolder.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(openFolder);
        _mainContent.Children.Add(new TextBlock { Text = "Поддерживаются сборки для Apple Silicon и Intel.", Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10) });
        _status.Text = "Настройки ника сохраняются автоматически на этом Mac.";
    }

    private static Border Card(string title, string subtitle) => new()
    {
        Background = Brush("#202631"),
        BorderBrush = Brush("#343D4D"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14),
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = subtitle, Foreground = Brush("#B8C1D0"), FontSize = 12 }
            }
        }
    };

    private async Task LoadVersionsAsync()
    {
        try
        {
            using var stream = await Http.GetStreamAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
            using var json = await JsonDocument.ParseAsync(stream);
            var releases = json.RootElement.GetProperty("versions").EnumerateArray()
                .Where(v => v.GetProperty("type").GetString() == "release")
                .Select(v => v.GetProperty("id").GetString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _versions.ItemsSource = releases;
                _versions.SelectedItem = releases.FirstOrDefault(v => v == "1.21.1") ?? releases.FirstOrDefault();
                _status.Text = $"Загружено официальных релизов Minecraft: {releases.Length}";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = $"Не удалось загрузить список версий: {ex.Message}");
        }
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
