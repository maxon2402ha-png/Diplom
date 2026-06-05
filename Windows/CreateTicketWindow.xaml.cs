using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class CreateTicketWindow : Window
    {
        private readonly AuthService _authService;

        private class PendingFile
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public long FileSize { get; set; }
            public string SizeLabel => AttachmentService.FormatSize(FileSize);
        }

        private readonly List<PendingFile> _pendingFiles = new();

        public CreateTicketWindow(AuthService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            Loaded += (s, e) => TitleTextBox.Focus();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            PickFiles();
        }

        private void DropZone_Click(object sender, MouseButtonEventArgs e)
        {
            PickFiles();
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.BorderBrush = FindResource("Brush.Primary") as Brush;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = FindResource("Brush.Border") as Brush;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = FindResource("Brush.Border") as Brush;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var f in files)
                TryAddFile(f);

            RefreshFileList();
        }

        private void PickFiles()
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Поддерживаемые файлы|*.png;*.jpg;*.jpeg;*.pdf;*.log;*.txt;*.docx;*.xlsx;*.zip",
                Title = "Выберите файлы"
            };

            if (dlg.ShowDialog() != true) return;
            foreach (var f in dlg.FileNames)
                TryAddFile(f);

            RefreshFileList();
        }

        private void TryAddFile(string path)
        {
            if (_pendingFiles.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return;

            var (ok, error) = AttachmentService.ValidateFile(path);
            if (!ok)
            {
                MessageBox.Show($"{Path.GetFileName(path)}: {error}", "Файл не принят",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pendingFiles.Add(new PendingFile
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                FileSize = new FileInfo(path).Length
            });
        }

        private void RemovePendingFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PendingFile pf)
            {
                _pendingFiles.Remove(pf);
                RefreshFileList();
            }
        }

        private void RefreshFileList()
        {
            SelectedFilesList.ItemsSource = null;
            SelectedFilesList.ItemsSource = _pendingFiles.ToList();
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentUser = _authService.CurrentUser;

                if (currentUser == null || currentUser.Role != Constants.UserRoles.Client)
                {
                    MessageBox.Show("Создание тикетов доступно только для клиентов!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TitleError.Visibility = Visibility.Collapsed;
                DescriptionError.Visibility = Visibility.Collapsed;

                bool invalid = false;
                if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
                {
                    TitleError.Text = "Укажите тему обращения";
                    TitleError.Visibility = Visibility.Visible;
                    invalid = true;
                }
                if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
                {
                    DescriptionError.Text = "Опишите проблему";
                    DescriptionError.Visibility = Visibility.Visible;
                    invalid = true;
                }
                if (invalid)
                {
                    if (string.IsNullOrWhiteSpace(TitleTextBox.Text)) TitleTextBox.Focus();
                    else DescriptionTextBox.Focus();
                    return;
                }

                SubmitButton.IsEnabled = false;
                SubmitButton.Content = "Анализ ИИ и Создание...";
                Cursor = Cursors.Wait;

                DateTime? due = null;
                if (DueDatePicker.SelectedDate.HasValue)
                {
                    var date = DueDatePicker.SelectedDate.Value;
                    var timeText = string.IsNullOrWhiteSpace(DueTimeBox.Text) ? "18:00" : DueTimeBox.Text.Trim();
                    if (TimeSpan.TryParse(timeText, out var ts))
                        due = date.Date.Add(ts).ToUniversalTime();
                }

                using var db = App.CreateDbContext();
                var ticketService = new TicketService(db);
                var notificationService = new NotificationService(db, _authService);

                var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == currentUser.Id);

                var newTicket = await ticketService.CreateAsync(
                    clientId: client!.Id,
                    title: TitleTextBox.Text.Trim(),
                    description: DescriptionTextBox.Text.Trim(),
                    manualDueAt: due,
                    authorUserId: currentUser.Id);

                foreach (var pf in _pendingFiles)
                {
                    try
                    {
                        await AttachmentService.SaveAsync(pf.FilePath, newTicket.Id, currentUser.Id, db);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Не удалось сохранить вложение {File}", pf.FileName);
                    }
                }

                notificationService.NotifyOperatorsAboutNewTicket(newTicket);

                MessageBox.Show(
                    $"Тикет #{newTicket.Id} успешно создан!\n\n✨ Нейросеть обработала заявку:\nКатегория: {newTicket.Category}\nПриоритет: {newTicket.Priority}",
                    "Успешно создано", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка:\n{ex.Message}", "Сбой", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (SubmitButton != null)
                {
                    SubmitButton.IsEnabled = true;
                    SubmitButton.Content = "Создать тикет";
                }
                Cursor = Cursors.Arrow;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
