using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NexcraftLauncher.Models;
using NexcraftLauncher.Services;

namespace NexcraftLauncher;

public partial class SettingsWindow : Window
{
    private readonly LauncherSettingsService _settingsService = new();
    private readonly LauncherSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        GameDirectoryBox.Text = _settings.GameDirectory;
        SetComboValue(MinimumMemoryBox, _settings.MemoryMinimumMb);
        SetComboValue(MaximumMemoryBox, _settings.MemoryMaximumMb);
        CloseLauncherCheckBox.IsChecked = _settings.CloseLauncherOnGameStart;
    }

    private static void SetComboValue(ComboBox comboBox, int value)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (int.TryParse(item.Content?.ToString(), out var itemValue) && itemValue == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static int GetComboValue(ComboBox comboBox, int fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var value))
            return value;

        return fallback;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для файлов Minecraft",
            Multiselect = false,
            InitialDirectory = Directory.Exists(GameDirectoryBox.Text)
                ? GameDirectoryBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        if (dialog.ShowDialog(this) == true)
            GameDirectoryBox.Text = dialog.FolderName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(GameDirectoryBox.Text))
        {
            ErrorText.Text = "Укажите папку игры.";
            return;
        }

        var minMemory = GetComboValue(MinimumMemoryBox, 1024);
        var maxMemory = GetComboValue(MaximumMemoryBox, 4096);
        if (maxMemory < minMemory)
        {
            ErrorText.Text = "Максимальная память должна быть не меньше минимальной.";
            return;
        }

        _settings.GameDirectory = GameDirectoryBox.Text.Trim();
        _settings.MemoryMinimumMb = minMemory;
        _settings.MemoryMaximumMb = maxMemory;
        _settings.CloseLauncherOnGameStart = CloseLauncherCheckBox.IsChecked == true;

        try
        {
            _settingsService.Save(_settings);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}