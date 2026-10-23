using System.Windows;
using System.Windows.Controls;

namespace NexcraftLauncher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        var version = (VersionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "не выбрана";
        StatusText.Text = $"Выбрано: {version}. Запуск пока не подключён — следующий этап: установка файлов игры и проверка Java.";
    }

    private void Mods_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Раздел модов появится позже. Он будет устанавливать и обновлять проверенную серверную сборку.", "NEXCRAFT — Моды и сборки", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Library_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Здесь появятся установленные версии Minecraft и сборки.", "NEXCRAFT — Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Настройки Java, памяти и папки игры добавим следующим этапом.", "NEXCRAFT — Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
}