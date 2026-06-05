using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class BackupManagerControl : UserControl
    {
        private readonly BackupService _service;

        public BackupManagerControl()
        {
            InitializeComponent();
            _service = new BackupService();
            BackupsPathText.Text = " " + BackupService.BackupsDirectory;
            Loaded += (_, _) => RefreshList();
        }

        private void RefreshList()
        {
            try
            {
                var list = _service.ListBackups();
                BackupsList.ItemsSource = list;
                NoBackupsText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList();

        private async void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            CreateBtn.IsEnabled = false;
            StatusText.Text = "Создание резервной копии...";
            try
            {
                var path = await _service.BackupAsync();
                _service.CleanupOld();
                StatusText.Text = $"Готово: {Path.GetFileName(path)}";
                RefreshList();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show(ex.Message, "Не удалось создать резервную копию",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CreateBtn.IsEnabled = true;
            }
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string path) return;

            var confirm = MessageBox.Show(
                $"Восстановить базу данных из:\n{Path.GetFileName(path)}\n\n" +
                "Текущие данные будут заменены данными из резервной копии. Продолжить?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            StatusText.Text = "Восстановление...";
            try
            {
                await _service.RestoreAsync(path);
                StatusText.Text = $"Восстановлено из: {Path.GetFileName(path)}";
                MessageBox.Show(
                    "Восстановление завершено. Рекомендуется перезапустить приложение.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show(ex.Message, "Не удалось восстановить",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string path) return;

            var confirm = MessageBox.Show(
                $"Удалить резервную копию {Path.GetFileName(path)}?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                BackupService.DeleteBackup(path);
                RefreshList();
                StatusText.Text = "Резервная копия удалена.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = BackupService.BackupsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
        }
    }
}
