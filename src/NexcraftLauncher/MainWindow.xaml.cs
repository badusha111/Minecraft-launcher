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
    private readonly LauncherSettings _settings;
    private bool _isInstalling;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        
        // Принудительно прячем кнопку входа Microsoft и текст, если они остались в XAML
        if (SignInButton != null) SignInButton.Visibility = Visibility.Collapsed;
        
        PlayerNicknameTextBox.Text = _settings.PlayerNickname;
        RestoreSelectedVersion();
        
        // Сразу активируем интерфейс под оффлайн-режим
        ModeDescriptionText.Text = "Свободный запуск Minecraft без лицензии. Доступна игра в одиночном режиме и на бесплатных серверах.";
        PlayButton.Content = "▶  ИГРАТЬ БЕЗ ЛИЦЕНЗИИ";
        StatusText.Text = "Введи свой ник, выбери версию игры и нажми играть.";
    }

    // Заглушки для кнопок переключения режимов (так как мы оставили только пиратку)
    private void MicrosoftMode_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "В NEXCRAFT доступен только оффлайн-режим игры.";
    }

    private void OfflineMode_Click(object sender, RoutedEventArgs e)
    {
        // Мы уже в оффлайн режиме по умолчанию
    }

    private void Library_Click(object sender, RoutedEventArgs e) { }
    private void Mods_Click(object sender, RoutedEventArgs e) { }
    private void Settings_Click(object sender, RoutedEventArgs e) { }

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

            // 🔥 ЖЕСТКИЙ СБРОС БЛОКИРОВКИ МУЛЬТИПЛЕЕРА ДЛЯ CMLIB V4
            // Поскольку обычное свойство заблокировано, мы используем переопределение 
            // аргументов лаунчера через добавление кастомных правил запуска.
            // Это принудительно заставит Java выполнить клиент с флагом --userType legacy.
            launchOption.ExtraArguments.Add("--userType");
            launchOption.ExtraArguments.Add("legacy");

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
