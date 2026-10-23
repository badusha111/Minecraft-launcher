using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace NexcraftLauncherMac;

public sealed class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private string _gameDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXCRAFT", "instances", "default");
    private int _minimumRamMb = 1024;
    private int _maximumRamMb = 4096;
    private bool _closeLauncherOnGameStart;
    private readonly List<Button> _navigationButtons = new();
    private readonly string _settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXCRAFT", "settings.json");
    private readonly StackPanel _mainContent = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { Text = "NEXCRAFT для macOS · готов к настройке", Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _versions = new() { MinWidth = 210, ItemsSource = new[] { "1.21.1", "1.20.1", "1.16.5" }, SelectedItem = "1.21.1" };
    private readonly TextBox _nickname = new() { Watermark = "Ник игрока", MaxLength = 16 };
    private readonly Button _play = new() { Content = "Играть", HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _login = new() { Content = "Войти через Microsoft", HorizontalContentAlignment = HorizontalAlignment.Center };
    private AuthenticatedMinecraftAccount? _account;
    private bool _isInstalling;
    private bool _isSigningIn;
    private string? _savedVersion;

    public MainWindow()
    {
        InitializeWindowShell();
        LoadSettings();
        _nickname.TextChanged += (_, _) => SaveSettings();
        _versions.SelectionChanged += (_, _) => SaveSettings();
        ShowHome();
        Opened += async (_, _) => await LoadVersionsAsync();
    }

    private void InitializeWindowShell()
    {
        Title = "NEXCRAFT Launcher";
        Width = 1280;
        Height = 780;
        MinWidth = 1040;
        MinHeight = 680;
        Background = Brush("#101319");
        Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var shell = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(18),
            Background = Brush("#151922"),
            BorderBrush = Brush("#303744"),
            BorderThickness = new Thickness(1)
        };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
        root.Children.Add(BuildSidebar());
        var right = new Grid { RowDefinitions = new RowDefinitions("62,*") };
        var header = new Grid { Margin = new Thickness(26, 0, 26, 0), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var brandHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        brandHeader.Children.Add(new TextBlock { Text = "NEXCRAFT", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brush("#F5F7FA"), VerticalAlignment = VerticalAlignment.Center });
        brandHeader.Children.Add(new Border { Width = 1, Height = 24, Background = Brush("#8B5CF6"), VerticalAlignment = VerticalAlignment.Center });
        brandHeader.Children.Add(new TextBlock { Text = "Официальный лаунчер МЕХМАТА", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush("#C084FC"), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(brandHeader);
        var ready = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        ready.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Brush("#19F58A"), VerticalAlignment = VerticalAlignment.Center });
        ready.Children.Add(new TextBlock { Text = "Готов к входу", FontSize = 12, Foreground = Brush("#E0E5ED"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(ready, 1);
        header.Children.Add(ready);
        right.Children.Add(header);

        var page = new StackPanel { Spacing = 0 };
        page.Children.Add(_mainContent);
        page.Children.Add(new Border { Height = 1, Background = Brush("#343D4D"), Margin = new Thickness(0, 8) });
        page.Children.Add(_status);
        var scroll = new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Thickness(26, 8, 26, 24)
        };
        Grid.SetRow(scroll, 1);
        right.Children.Add(scroll);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        shell.Child = root;
        Content = shell;
    }

    private Control BuildSidebar()
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("84,*,72") };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22, 0, 0, 0),
            Spacing = 10
        };
        brand.Children.Add(new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
            Background = Brush("#173C31"), BorderBrush = Brush("#19F58A"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "N", FontSize = 22, FontWeight = FontWeight.Black, Foreground = Brush("#19F58A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        brand.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "NEXCRAFT", FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "MINECRAFT LAUNCHER", FontSize = 9, Foreground = Brush("#AEB8C8") }
            }
        });
        layout.Children.Add(brand);

        var menu = new StackPanel { Spacing = 3, Margin = new Thickness(12, 12, 12, 0) };
        menu.Children.Add(NavButton("⌂   Главная", ShowHome, true));
        menu.Children.Add(NavButton("◇   Моды и сборки", ShowMods, true));
        menu.Children.Add(NavButton("▣   Библиотека", ShowLibrary, true));
        menu.Children.Add(NavButton("⚙   Настройки", ShowSettings, true));
        Grid.SetRow(menu, 1);
        layout.Children.Add(menu);

        var footer = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0) , Spacing = 5 };
        footer.Children.Add(new TextBlock { Text = "NEXCRAFT PREVIEW", FontSize = 10, Foreground = Brush("#AEB8C8") });
        footer.Children.Add(new TextBlock { Text = "РАННЯЯ ВЕРСИЯ", FontSize = 9, Foreground = Brush("#FF9D45") });
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        return new Border { Background = Brush("#0C1222"), Child = layout };
    }

    private Button NavButton(string title, Action action, bool navigation = false)
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
        button.Click += (_, _) =>
        {
            if (navigation) SetActiveNavigation(button);
            action();
        };
        if (navigation) _navigationButtons.Add(button);
        return button;
    }

    private void SetActiveNavigation(Button active)
    {
        foreach (var button in _navigationButtons)
        {
            var selected = ReferenceEquals(button, active);
            button.Background = Brush(selected ? "#25213A" : "#151922");
            button.Foreground = Brush(selected ? "#B99AFF" : "#C7CEDA");
            button.BorderBrush = Brush(selected ? "#51427D" : "#151922");
        }
    }

    private void PreparePage(string title, string subtitle)
    {
        _mainContent.Children.Clear();
        _mainContent.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        _mainContent.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap });
    }

    private void ShowHome()
    {
        if (_navigationButtons.Count > 0) SetActiveNavigation(_navigationButtons[0]);
        PreparePage("", "");
        _mainContent.Spacing = 0;

        var heroGrid = new Grid();
        heroGrid.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#211447"), 0),
                new GradientStop(Color.Parse("#171A35"), 0.48),
                new GradientStop(Color.Parse("#102B32"), 1)
            }
        };
        var hero = new Border
        {
            Height = 270,
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            BorderBrush = Brush("#5B4B36"),
            BorderThickness = new Thickness(1),
            Child = heroGrid
        };
        var landscape = new Canvas { IsHitTestVisible = true, Opacity = 0.95 };
        void AddLandscape(Control shape, double left, double top) { Canvas.SetLeft(shape, left); Canvas.SetTop(shape, top); landscape.Children.Add(shape); }
        AddLandscape(new Rectangle { Width = 54, Height = 12, Fill = Brush("#C8C8FF"), Opacity = 0.25 }, 350, 26);
        AddLandscape(new Rectangle { Width = 76, Height = 12, Fill = Brush("#C8C8FF"), Opacity = 0.25 }, 376, 14);
        AddLandscape(new Ellipse { Width = 58, Height = 58, Fill = Brush("#FFCB77"), Opacity = 0.85 }, 590, 26);
        landscape.Children.Add(new Polygon { Points = new Avalonia.Collections.AvaloniaList<Avalonia.Point> { new(300,178), new(420,70), new(525,178) }, Fill = Brush("#4A527B") });
        landscape.Children.Add(new Polygon { Points = new Avalonia.Collections.AvaloniaList<Avalonia.Point> { new(400,185), new(520,90), new(635,185) }, Fill = Brush("#35436A") });
        landscape.Children.Add(new Polygon { Points = new Avalonia.Collections.AvaloniaList<Avalonia.Point> { new(450,218), new(585,128), new(735,218), new(735,258), new(450,258) }, Fill = Brush("#315C50") });
        landscape.Children.Add(new Polygon { Points = new Avalonia.Collections.AvaloniaList<Avalonia.Point> { new(450,218), new(585,128), new(735,218) }, Fill = Brush("#46755C") });
        AddLandscape(new Rectangle { Width = 240, Height = 8, Fill = Brush("#5B8D62") }, 465, 223);
        AddLandscape(new Rectangle { Width = 14, Height = 30, Fill = Brush("#6B4938") }, 520, 205);
        AddLandscape(new Rectangle { Width = 42, Height = 22, Fill = Brush("#3E8A51") }, 506, 188);
        AddLandscape(new Rectangle { Width = 26, Height = 15, Fill = Brush("#4B9B59") }, 514, 176);
        AddLandscape(new Rectangle { Width = 12, Height = 24, Fill = Brush("#6B4938") }, 585, 213);
        AddLandscape(new Rectangle { Width = 40, Height = 21, Fill = Brush("#347849") }, 571, 198);
        AddLandscape(new Rectangle { Width = 24, Height = 15, Fill = Brush("#4A9654") }, 579, 186);
        heroGrid.Children.Add(landscape);
        heroGrid.Children.Add(new Border { Width = 8, Background = Brush("#8B5CF6"), HorizontalAlignment = HorizontalAlignment.Left });
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(34, 0, 0, 0), Spacing = 4 };
        heroText.Children.Add(new Border
        {
            Background = Brush("#1B2029"), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 11),
            Child = new TextBlock { Text = "NEXCRAFT — ЭТО НЕ ПРОСТО ЛАУНЧЕР", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brush("#19F58A") }
        });
        heroText.Children.Add(new TextBlock { Text = "ТВОЙ МИР.", FontSize = 39, FontWeight = FontWeight.Black, Foreground = Brushes.White });
        heroText.Children.Add(new TextBlock { Text = "ТВОИ ПРАВИЛА.", FontSize = 39, FontWeight = FontWeight.Black, Foreground = Brush("#B45CFF") });
        heroText.Children.Add(new TextBlock { Text = "Это твой мир, твои правила.", FontSize = 14, Foreground = Brush("#E2E7EF"), Margin = new Thickness(0, 10, 0, 0) });
        heroGrid.Children.Add(heroText);
        heroGrid.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 20, 0), BorderBrush = Brush("#687588"), BorderThickness = new Thickness(1),
            Background = Brush("#70202731"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8),
            Child = new TextBlock { Text = "JAVA EDITION", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brushes.White }
        });
        heroGrid.Children.Add(new TextBlock { Text = "NEXCRAFT  /  SEASON 01", VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 22, 18), FontSize = 10, Foreground = Brush("#D2D9E4") });
        _mainContent.Children.Add(hero);

        var contentRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,240"), Margin = new Thickness(0, 20, 0, 0) };
        var profile = new StackPanel { Spacing = 8 };
        profile.Children.Add(new TextBlock { Text = "ТВОЙ ПРОФИЛЬ", FontSize = 11, Foreground = Brush("#B99AFF") });
        profile.Children.Add(new TextBlock { Text = "Запуск Minecraft", FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        profile.Children.Add(new TextBlock { Text = "Твой игровой ник", FontSize = 12, Foreground = Brush("#D0D7E2") });
        _nickname.Height = 40;
        _nickname.Padding = new Thickness(10, 8);
        _nickname.Background = Brush("#202631");
        _nickname.Foreground = Brushes.White;
        _nickname.BorderBrush = Brush("#586477");
        _nickname.BorderThickness = new Thickness(1);
        profile.Children.Add(_nickname);
        var versionLabel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        versionLabel.Children.Add(new TextBlock { Text = "ВЕРСИЯ MINECRAFT", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#B99AFF"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(new Border { Background = Brush("#29213D"), BorderBrush = Brush("#6B4DFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3), Child = new TextBlock { Text = "JAVA", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Brush("#D9CCFF") } }, 1);
        var javaBadge = versionLabel.Children[0] as Control;
        var badge = new Border { Background = Brush("#29213D"), BorderBrush = Brush("#6B4DFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3), Child = new TextBlock { Text = "JAVA", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Brush("#D9CCFF") } };
        Grid.SetColumn(badge, 1);
        versionLabel.Children.Add(badge);
        profile.Children.Add(versionLabel);
        _versions.Height = 42;
        _versions.Background = Brush("#202631");
        _versions.Foreground = Brush("#FFB86B");
        _versions.BorderBrush = Brush("#8B6BFF");
        profile.Children.Add(_versions);
        profile.Children.Add(new TextBlock { Text = "Официальный каталог Mojang", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#AEB8C8"), FontSize = 11 });
        _login.Background = Brush("#29243B");
        _login.Foreground = Brushes.White;
        _login.Padding = new Thickness(14, 10);
        _login.HorizontalContentAlignment = HorizontalAlignment.Center;
        _login.Click -= LoginWithMicrosoft;
        _login.Click += LoginWithMicrosoft;
        profile.Children.Add(_login);
        var profileCard = new Border { Background = Brush("#20232D"), CornerRadius = new CornerRadius(14), Padding = new Thickness(20), Margin = new Thickness(0, 0, 16, 0), BorderBrush = Brush("#303744"), BorderThickness = new Thickness(1), Child = profile };
        contentRow.Children.Add(profileCard);

        var right = new StackPanel { Spacing = 12 };
        var playerCard = new Border
        {
            Background = Brush("#171B2B"), BorderBrush = Brush("#3A315E"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "ИГРОВОЙ ПРОФИЛЬ", FontSize = 10, Foreground = Brush("#C084FC") },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 12,
                        Children =
                        {
                            new Grid
                            {
                                Width = 54, Height = 54,
                                Children =
                                {
                                    new Border { Background = Brush("#35245B"), CornerRadius = new CornerRadius(8) },
                                    new Rectangle { Width = 20, Height = 20, Fill = Brush("#E5B18B"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) },
                                    new Rectangle { Width = 24, Height = 8, Fill = Brush("#5A372B"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) },
                                    new Rectangle { Width = 30, Height = 17, Fill = Brush("#6D5BEA"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) }
                                }
                            },
                            new StackPanel
                            {
                                VerticalAlignment = VerticalAlignment.Center, Spacing = 4,
                                Children =
                                {
                                    new TextBlock { Text = "ИГРОК NEXCRAFT", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#F5F7FA") },
                                    new TextBlock { Text = "Java Edition", FontSize = 11, Foreground = Brush("#AEB8C8") },
                                    new TextBlock { Text = "Профиль запуска", FontSize = 10, Foreground = Brush("#19F58A") }
                                }
                            }
                        }
                    }
                }
            }
        };
        right.Children.Add(playerCard);
        right.Children.Add(new Border
        {
            Background = Brush("#1A2825"), BorderBrush = Brush("#28644A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = "СЕРВЕРНАЯ СБОРКА", FontSize = 10, Foreground = Brush("#19F58A") },
                    new TextBlock { Text = "Моды — скоро", FontSize = 17, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Подготовим автоматическую установку и обновление модов.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#D0D8E0"), FontSize = 12 }
                }
            }
        });
        _play.Height = 66;
        _play.Background = Brush("#19F58A");
        _play.Foreground = Brush("#092318");
        _play.BorderThickness = new Thickness(0);
        _play.FontSize = 25;
        _play.FontWeight = FontWeight.Black;
        _play.HorizontalContentAlignment = HorizontalAlignment.Center;
        _play.Click -= PlayMinecraft;
        _play.Click += PlayMinecraft;
        right.Children.Add(_play);
        right.Children.Add(new TextBlock { Text = "ПЕРВАЯ УСТАНОВКА СКАЧАЕТ ФАЙЛЫ ИГРЫ", TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#C0C8D5"), FontSize = 10, Margin = new Thickness(0, 0, 0, 8) });
        Grid.SetColumn(right, 1);
        contentRow.Children.Add(right);
        _mainContent.Children.Add(contentRow);
        _status.Text = _account is null
            ? "Войди через Microsoft с аккаунтом, у которого есть Minecraft: Java Edition."
            : $"Вход выполнен: {_account.Username}. Выбери версию и нажми «ИГРАТЬ».";
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

    private async void PlayMinecraft(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
                MinimumRamMb = _minimumRamMb,
                MaximumRamMb = _maximumRamMb
            };
            var process = await launcher.InstallAndBuildProcessAsync(version, launchOptions);
            process.Start();
            _status.Text = $"Minecraft {version} запущен для аккаунта {_account.Username}.";
            if (_closeLauncherOnGameStart) Close();
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
            if (document.RootElement.TryGetProperty("gameDirectory", out var gameDirectory) && !string.IsNullOrWhiteSpace(gameDirectory.GetString()))
                _gameDirectory = gameDirectory.GetString()!;
            if (document.RootElement.TryGetProperty("minimumRamMb", out var minimumRam) && minimumRam.TryGetInt32(out var min))
                _minimumRamMb = Math.Clamp(min, 512, 65536);
            if (document.RootElement.TryGetProperty("maximumRamMb", out var maximumRam) && maximumRam.TryGetInt32(out var max))
                _maximumRamMb = Math.Clamp(max, _minimumRamMb, 65536);
            if (document.RootElement.TryGetProperty("closeLauncherOnGameStart", out var closeLauncher) && (closeLauncher.ValueKind == JsonValueKind.True || closeLauncher.ValueKind == JsonValueKind.False))
                _closeLauncherOnGameStart = closeLauncher.GetBoolean();
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
                selectedVersion = _versions.SelectedItem?.ToString() ?? _savedVersion,
                gameDirectory = _gameDirectory,
                minimumRamMb = _minimumRamMb,
                maximumRamMb = _maximumRamMb,
                closeLauncherOnGameStart = _closeLauncherOnGameStart
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
        var refresh = NavButton("Обновить список модов", ShowMods);
        refresh.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(refresh);
        var open = NavButton("Открыть папку модов", () => OpenFolder(Path.Combine(_gameDirectory, "mods")));
        open.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(open);
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
        var refresh = NavButton("Обновить библиотеку", ShowLibrary);
        refresh.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(refresh);
        var open = NavButton("Открыть папку версий", () => OpenFolder(Path.Combine(_gameDirectory, "versions")));
        open.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(open);
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
        PreparePage("Настройки", "Подстрой игру под свой компьютер — параметры сохраняются автоматически.");
        _mainContent.Children.Add(new TextBlock { Text = "Папка игры", Foreground = Brush("#B8C1D0"), Margin = new Thickness(0, 8, 0, 4) });
        var gamePath = new TextBox { Text = _gameDirectory, Watermark = "Путь к папке Minecraft", Padding = new Thickness(10, 8), Background = Brush("#202631"), Foreground = Brushes.White, BorderBrush = Brush("#586477") };
        _mainContent.Children.Add(gamePath);
        var folderActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var browseGame = NavButton("Обзор…", () => _ = BrowseGameFolderAsync(gamePath));
        browseGame.HorizontalContentAlignment = HorizontalAlignment.Center;
        folderActions.Children.Add(browseGame);
        var openGame = NavButton("Открыть папку в Finder", () => OpenFolder(gamePath.Text ?? _gameDirectory));
        openGame.HorizontalContentAlignment = HorizontalAlignment.Center;
        folderActions.Children.Add(openGame);
        var applyPath = NavButton("Сохранить путь", () =>
        {
            var candidate = gamePath.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(candidate)) { _status.Text = "Укажи путь к папке игры."; return; }
            _gameDirectory = Path.GetFullPath(candidate);
            try { Directory.CreateDirectory(_gameDirectory); }
            catch (Exception ex) { _status.Text = $"Не удалось создать папку игры: {ex.Message}"; return; }
            SaveSettings();
            _status.Text = $"Папка игры сохранена: {_gameDirectory}";
        });
        applyPath.HorizontalContentAlignment = HorizontalAlignment.Center;
        folderActions.Children.Add(applyPath);
        _mainContent.Children.Add(folderActions);

        _mainContent.Children.Add(new TextBlock { Text = "Память для Minecraft (МБ)", Foreground = Brush("#B8C1D0"), Margin = new Thickness(0, 14, 0, 4) });
        var memoryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var minimum = new ComboBox { ItemsSource = new[] { 512, 1024, 2048, 4096 }, SelectedItem = _minimumRamMb, Background = Brush("#202631"), Foreground = Brushes.White, Padding = new Thickness(8) };
        var maximum = new ComboBox { ItemsSource = new[] { 2048, 4096, 6144, 8192, 12288, 16384 }, SelectedItem = _maximumRamMb, Background = Brush("#202631"), Foreground = Brushes.White, Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0) };
        var minPanel = new StackPanel { Spacing = 5 };
        minPanel.Children.Add(new TextBlock { Text = "Минимум", FontSize = 12, Foreground = Brush("#C0C8D5") });
        minPanel.Children.Add(minimum);
        var maxPanel = new StackPanel { Spacing = 5 };
        maxPanel.Children.Add(new TextBlock { Text = "Максимум", FontSize = 12, Foreground = Brush("#C0C8D5") });
        maxPanel.Children.Add(maximum);
        Grid.SetColumn(maxPanel, 1);
        memoryRow.Children.Add(minPanel);
        memoryRow.Children.Add(maxPanel);
        _mainContent.Children.Add(memoryRow);

        var closeLauncher = new CheckBox { Content = "Закрывать лаунчер после запуска игры", IsChecked = _closeLauncherOnGameStart, Foreground = Brush("#E0E5ED"), Margin = new Thickness(0, 14, 0, 0) };
        _mainContent.Children.Add(closeLauncher);
        var save = NavButton("Сохранить настройки", () =>
        {
            if (!int.TryParse(minimum.SelectedItem?.ToString(), out var min) ||
                !int.TryParse(maximum.SelectedItem?.ToString(), out var max) || max < min)
            { _status.Text = "Максимальная память должна быть не меньше минимальной."; return; }
            if (string.IsNullOrWhiteSpace(gamePath.Text)) { _status.Text = "Укажи папку игры."; return; }
            _gameDirectory = Path.GetFullPath(gamePath.Text.Trim());
            _minimumRamMb = min;
            _maximumRamMb = max;
            _closeLauncherOnGameStart = closeLauncher.IsChecked == true;
            try { Directory.CreateDirectory(_gameDirectory); }
            catch (Exception ex) { _status.Text = $"Не удалось создать папку игры: {ex.Message}"; return; }
            SaveSettings();
            _status.Text = "Настройки сохранены.";
        });
        save.Background = Brush("#19F58A");
        save.Foreground = Brush("#092318");
        save.FontWeight = FontWeight.Bold;
        save.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(save);
        _mainContent.Children.Add(new TextBlock { Text = "Для современных версий Minecraft обычно требуется Java 21.", Foreground = Brush("#B8C1D0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var checkJava = NavButton("Проверить установленную Java", () => _ = CheckJavaAsync());
        checkJava.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainContent.Children.Add(checkJava);
        _status.Text = "Настройки лаунчера.";
    }

    private async Task BrowseGameFolderAsync(TextBox target)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Выберите папку для файлов Minecraft",
                AllowMultiple = false
            });
            if (folders.Count > 0)
                target.Text = folders[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            _status.Text = $"Не удалось открыть выбор папки: {ex.Message}";
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = "open", ArgumentList = { path }, UseShellExecute = false });
            _status.Text = $"Открыта папка: {path}";
        }
        catch (Exception ex) { _status.Text = $"Не удалось открыть папку: {ex.Message}"; }
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
