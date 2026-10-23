using System.Windows;
using System.Windows.Controls;
using NexcraftLauncher.Models;
using NexcraftLauncher.Services;

namespace NexcraftLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherSettingsService _settingsService = new();
    private readonly MinecraftMetadataService _metadataService = new();
    private readonly JavaRuntimeService _javaRuntimeService = new();
    private LauncherSettings _settings;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        RestoreSelectedVersion();
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

        _settings.SelectedMinecraftVersion = label.Split('—', StringSplitOptions.TrimEntries)[0];
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedVersion();
        var version = (VersionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Split('—', StringSplitOptions.TrimEntries)[0];
        if (string.IsNullOrWhiteSpace(version))
        {
            StatusText.Text = "Сначала выбери версию Minecraft.";
            return;
        }

        StatusText.Text = "Проверяю официальные метаданные Minecraft…";
        try
        {
            var metadata = await _metadataService.GetVersionMetadataAsync(version);
            StatusText.Text = $"Версия {metadata.Id} найдена. Проверяю Java…";
            var java = await _javaRuntimeService.DetectAsync();
            var requiredJava = metadata.JavaVersion?.MajorVersion;
            if (java is null)
            {
                StatusText.Text = requiredJava is int required
                    ? $"Метаданные {metadata.Id} проверены. Java {required} не найдена. Установка и запуск игры ещё не подключены."
                    : $"Метаданные {metadata.Id} проверены, но Java не найдена. Установка и запуск игры ещё не подключены.";
                return;
            }

            if (requiredJava is int requiredVersion && java.MajorVersion < requiredVersion)
            {
                StatusText.Text = $"Найдена Java {java.MajorVersion}, а Minecraft {metadata.Id} требует Java {requiredVersion}. Запуск пока недоступен.";
                return;
            }

            StatusText.Text = $"Проверка пройдена: Minecraft {metadata.Id}, Java {java.MajorVersion}. Загрузка игровых файлов и запуск — следующий этап.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Проверка отменена.";
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"Не удалось получить данные Mojang: {ex.Message}";
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = $"Не удалось проверить версию: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "Сервер метаданных не ответил вовремя. Попробуй ещё раз.";
        }
    }

    private void Mods_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Раздел модов появится позже. Он будет устанавливать и обновлять проверенную серверную сборку.", "NEXCRAFT — Моды и сборки", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Library_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Здесь появятся установленные версии Minecraft и сборки.", "NEXCRAFT — Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = _settingsService.Load();
            StatusText.Text = "Настройки сохранены.";
        }
    }
}