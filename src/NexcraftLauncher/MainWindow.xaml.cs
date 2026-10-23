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
    private readonly JavaRuntimeService _javaRuntimeService = new();
    private readonly MinecraftInstallationService _installationService = new();
    private readonly MicrosoftMinecraftAuthService _authService = new();
    private readonly MinecraftLaunchService _launchService = new();
    private AuthenticatedMinecraftAccount? _account;
    private bool _isInstalling;
    private bool _offlineMode;
    private LauncherSettings _settings;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        PlayerNicknameTextBox.Text = _settings.PlayerNickname;
        RestoreSelectedVersion();
    }

    private void MicrosoftMode_Click(object sender, RoutedEventArgs e)
    {
        _offlineMode = false;
        MicrosoftModeButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#20352F"));
        MicrosoftModeButton.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#19F58A"));
        OfflineModeButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#272D3A"));
        OfflineModeButton.Foreground = System.Windows.Media.Brushes.White;
        ModeDescriptionText.Text = "Официальный вход Microsoft, проверка доступа и запуск Minecraft Java Edition.";
        SignInButton.Visibility = Visibility.Visible;
        PlayButton.Content = "▶  ИГРАТЬ";
        StatusText.Text = _account is null ? "Войди через Microsoft, чтобы установить и запустить Minecraft." : $"Вход выполнен: {_account.Username}.";
    }

    private void OfflineMode_Click(object sender, RoutedEventArgs e)
    {
        _offlineMode = true;
        OfflineModeButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#30264B"));
        OfflineModeButton.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C8B6FF"));
        MicrosoftModeButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#272D3A"));
        MicrosoftModeButton.Foreground = System.Windows.Media.Brushes.White;
        ModeDescriptionText.Text = "Локальный ник без Microsoft. Свободное скачивание файлов и запуск игры в оффлайн-режиме.";
        SignInButton.Visibility = Visibility.Collapsed;
        PlayButton.Content = "▶  ИГРАТЬ БЕЗ ЛИЦЕНЗИИ";
        StatusText.Text = "Введи локальный ник, выбери версию и нажми играть.";
    }

    // Заглушки для кнопок из интерфейса XAML, чтобы сборка не вылетала
    private void Library_Click(object sender, RoutedEventArgs e)
    {
        // Будет реализовано позже
    }

    private void Mods_Click(object sender, RoutedEventArgs e)
    {
        // Будет реализовано позже
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
        if (string.IsNullOrWhiteSpace(label))
            return;

        var parts = label.Split('—', StringSplitOptions.TrimEntries);
        if (parts.Length > 0)
        {
            _settings.SelectedMinecraftVersion = parts[0];
            try
            {
                _settingsService.Save(_settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = $"Не удалось сохранить настройки: {ex.Message}";
            }
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling || _account is not null) return;
        SignInButton.IsEnabled = false;
        try
        {
            var status = new Progress<string>(message => StatusText.Text = message);
            _account = await _authService.SignInAsync(
                message => MessageBox.Show(this, message + "\\n\\nПосле подтверждения входа вернись в NEXCRAFT.",
                    "NEXCRAFT — Вход в Minecraft", MessageBoxButton.OK, MessageBoxImage.Information),
                status);
            PlayerNicknameTextBox.Text = _account.Username;
            _settings.PlayerNickname = _account.Username;
            _settingsService.Save(_settings);
            StatusText.Text = $"Вход выполнен: {_account.Username}. Теперь выбери версию и нажми «Играть».";
            SignInButton.Content = "✓ Вход выполнен";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Вход отменён.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось войти: {ex.Message}";
        }
        finally
        {
            SignInButton.IsEnabled = _account is null;
        }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        var nickname = PlayerNicknameTextBox.Text.Trim();
        
        if (!System.Text.RegularExpressions.Regex.IsMatch(nickname, @"^[A-Za-z0-9_]{3,16}$"))
        {
            StatusText.Text = "Введи ник длиной 3–16 символов: латинские буквы, цифры или подчёркивание.";
            PlayerNicknameTextBox.Focus();
            return;
        }

        _settings.PlayerNickname = nickname;
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Не удалось сохранить ник: {ex.Message}";
            return;
        }

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
                    StatusText.Text = $"Проверка файлов: {pArgs.ProgressedTasks}/{pArgs.TotalTasks} ({pArgs.Percent}%)";
                });
            };

            CmlLib.Core.Auth.MinecraftSession session;

            if (_offlineMode)
            {
                StatusText.Text = "Создание автономного профиля...";
                session = CmlLib.Core.Auth.MinecraftSession.CreateOfflineSession(nickname);
            }
            else
            {
                if (_account is null)
                {
                    StatusText.Text = "Сначала войдите через Microsoft или переключитесь в Офлайн-режим.";
                    _isInstalling = false;
                    PlayButton.IsEnabled = true;
                    return;
                }
                StatusText.Text = "Авторизация официальной сессии...";
                session = new CmlLib.Core.Auth.MinecraftSession(_account.Username, _account.AccessToken, _account.Uuid, "msa");
            }

            // Безопасные дефолтные настройки памяти (4ГБ макс, 1ГБ мин), чтобы не зависеть от переменных
            var launchOption = new MLaunchOption
            {
                MaximumRamMb = 4096,
                MinimumRamMb = 1024,
                Session = session
            };

            StatusText.Text = "Анализ необходимых файлов и загрузка компонентов...";
            
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
