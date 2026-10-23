using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

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
    private readonly Button _login = new() { Content = "Войти через Microsoft", HorizontalContentAlignment = HorizontalAlignment.Center };
    private AuthenticatedMinecraftAccount? _account;
    private bool _isInstalling;
    private bool _isSigningIn;
    private string? _savedVersion;

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
        _login.Background = Brush("#29243B");
        _login.Foreground = Brushes.White;
        _login.Padding = new Thickness(18, 12);
        _login.Click -= LoginWithMicrosoft;
        _login.Click += LoginWithMicrosoft;
        _mainContent.Children.Add(_login);
        _play.Background = Brush("#7C4DFF");
        _play.Foreground = Brushes.White;
        _play.Padding = new Thickness(18, 12);
        _play.Click -= PlayNotReady;
        _play.Click += PlayNotReady;
        _mainContent.Children.Add(_play);
        _status.Text = _account is null
            ? "Войди через Microsoft с аккаунтом, у которого есть Minecraft: Java Edition."
            : $"Вход выполнен: {_account.Username}. Выбери версию и нажми «Играть».";
    }

    private async void LoginWithMicrosoft(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isSigningIn || _isInstalling) return;
        _isSigningIn = true;
        _login.IsEnabled = false;
        _login.Content = "Подключение…";
        try
        {
            var auth = new MicrosoftMinecraftAuthService();
            var progress = new Progress<string>(message => Dispatcher.UIThread.Post(() => _status.Text = message));
            _account = await auth.SignInAsync(
                message => Dispatcher.UIThread.Post(() => _status.Text = message),
                progress);
            _nickname.Text = _account.Username;
            SaveSettings();
            _status.Text = $"Вход выполнен: {_account.Username}. Лицензия Minecraft Java Edition подтверждена.";
            _login.Content = $"Выполнен вход: {_account.Username}";
        }
        catch (Exception ex)
        {
            _account = null;
            _status.Text = $"Не удалось войти через Microsoft: {ex.Message}";
            _login.Content = "Повторить вход Microsoft";
        }
        finally
        {
            _isSigningIn = false;
            _login.IsEnabled = true;
        }
    }

    private async void PlayNotReady(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInstalling || _isSigningIn) return;
        if (_account is null)
        {
            _status.Text = "Сначала нажми «Войти через Microsoft» и подтверди вход в браузере. Нужна лицензия Minecraft: Java Edition.";
            return;
        }

        var nickname = _account.Username;
        if (!System.Text.RegularExpressions.Regex.IsMatch(nickname, @"^[A-Za-z0-9_]{3,16}$"))
        {
            _status.Text = "Введи ник длиной 3–16 символов (латиница, цифры и подчёркивание), затем установи версию.";
            return;
        }

        var version = _versions.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(version))
        {
            _status.Text = "Сначала выбери официальную версию Minecraft.";
            return;
        }

        SaveSettings();
        _isInstalling = true;
        _play.IsEnabled = false;
        _play.Content = "Подготовка…";
        try
        {
            Directory.CreateDirectory(_gameDirectory);
            var path = new MinecraftPath(_gameDirectory);
            var launcher = new MinecraftLauncher(path);
            launcher.FileProgressChanged += (_, args) =>
            {
                var percentage = args.TotalTasks > 0 ? (int)((double)args.ProgressedTasks / args.TotalTasks * 100) : 0;
                Dispatcher.UIThread.Post(() => _status.Text = $"Установка Minecraft {version}: {args.ProgressedTasks}/{args.TotalTasks} ({percentage}%)");
            };

            _status.Text = $"Подготавливаем официальные файлы Minecraft {version}…";
            _status.Text = $"Загружаю и проверяю файлы Minecraft {version}…";
            await launcher.InstallAsync(version);

            _status.Text = "Подготавливаю официальный профиль и запускаю Minecraft…";
            var session = new MSession(_account.Username, _account.AccessToken, _account.Uuid);
            session.UserType = "msa";
            var launchOptions = new MLaunchOption
            {
                Session = session,
                MinimumRamMb = 1024,
                MaximumRamMb = 4096
            };
            var process = await launcher.InstallAndBuildProcessAsync(version, launchOptions);
            process.Start();
            _status.Text = $"Minecraft {version} запущен для аккаунта {_account.Username}.";
            _play.Content = "Играть";
        }
        catch (Exception ex)
        {
            _status.Text = $"Не удалось установить Minecraft: {ex.Message}";
            _play.Content = "Повторить установку";
        }
        finally
        {
            _isInstalling = false;
            _play.IsEnabled = true;
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsFile));
            if (document.RootElement.TryGetProperty("nickname", out var nickname))
                _nickname.Text = nickname.GetString() ?? string.Empty;
            if (document.RootElement.TryGetProperty("selectedVersion", out var selectedVersion))
                _savedVersion = selectedVersion.GetString();
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
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new
            {
                nickname = _nickname.Text?.Trim() ?? string.Empty,
                selectedVersion = _versions.SelectedItem?.ToString() ?? _savedVersion
            }, new JsonSerializerOptions { WriteIndented = true }));
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
        _mainContent.Children.Add(new TextBlock { Text = "Java", Foreground = Brush("#B8C1D0"), Margin = new Thickness(0, 10, 0, 0) });
        var checkJava = NavButton("Проверить установленную Java", () => _ = CheckJavaAsync());
        checkJava.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(checkJava);
        _mainContent.Children.Add(new TextBlock { Text = "Поддерживаются сборки для Apple Silicon и Intel.", Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10) });
        _status.Text = "Ник и выбранная версия сохраняются автоматически на этом Mac.";
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

    private async Task CheckJavaAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _status.Text = "Не удалось запустить проверку Java.";
                return;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await stderrTask) + (await stdoutTask);
            output = output.Trim();
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                    ? $"Java обнаружена: {output.Split((char)10, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}"
                    : "Java не найдена или не запускается. Для современных версий Minecraft обычно требуется Java 21; для старых версий требования отличаются.");
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = "Java не найдена. Для современных версий Minecraft обычно требуется Java 21; для старых версий требования отличаются.");
        }
    }

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
                _versions.SelectedItem = releases.FirstOrDefault(v => v == _savedVersion)
                    ?? releases.FirstOrDefault(v => v == "1.21.1")
                    ?? releases.FirstOrDefault();
                _versions.SelectionChanged += (_, _) => SaveSettings();
                _savedVersion = _versions.SelectedItem?.ToString();
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
