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
    private readonly MinecraftInstallationService _installationService = new();
    private bool _isInstalling;
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
        if (_isInstalling) return;
        SaveSelectedVersion();
        var version = (VersionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Split('—', StringSplitOptions.TrimEntries)[0];
        if (string.IsNullOrWhiteSpace(version))
        {
            StatusText.Text = "Сначала выбери версию Minecraft.";
            return;
        }

        _isInstalling = true;
        PlayButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        try
        {
            StatusText.Text = "Подготавливаю установку…";
            var status = new Progress<string>(message => StatusText.Text = message);
            var progress = new Progress<double>(value => DownloadProgress.Value = Math.Clamp(value * 100, 0, 100));
            await _installationService.InstallAsync(version, _settings.GameDirectory, status, progress);
            StatusText.Text = $"Minecraft {version}: клиент, библиотеки и ресурсы загружены. Для онлайн-игры ещё нужна авторизация Microsoft и настройка запуска.";
            MessageBox.Show(
                $"Файлы Minecraft {version} загружены и проверены.\n\nПапка игры: {_settings.GameDirectory}\n\nСледующий этап — авторизация Microsoft и полноценный запуск.",
                "NEXCRAFT — Установка завершена", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Установка отменена.";
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"Ошибка сети при загрузке: {ex.Message}";
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = $"Проверка файлов не пройдена: {ex.Message}";
        }
        catch (IOException ex)
        {
            StatusText.Text = $"Не удалось записать файлы игры: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText.Text = $"Нет доступа к папке игры: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка установки: {ex.Message}";
        }
        finally
        {
            _isInstalling = false;
            PlayButton.IsEnabled = true;
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