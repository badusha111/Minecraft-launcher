using System.Windows;
using System.Windows.Controls;
using NexcraftLauncher.Models;
using NexcraftLauncher.Services;

namespace NexcraftLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherSettingsService _settingsService = new();
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

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedVersion();
        var version = (VersionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "не выбрана";
        StatusText.Text = $"Выбрано: {version}. Запуск пока не подключён — следующий этап: установка файлов игры и проверка Java.";
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