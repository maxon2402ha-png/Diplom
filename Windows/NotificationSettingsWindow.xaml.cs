using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Linq;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    public partial class NotificationSettingsWindow : Window
    {
        private readonly int _currentUserId;
        private NotificationSettings _settings = null!; 
        public NotificationSettingsWindow(int userId)
        {
            InitializeComponent();
            _currentUserId = userId;
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                using var db = new AppDbContext();

                _settings = db.NotificationSettings
                    .FirstOrDefault(s => s.UserId == _currentUserId)
                    ?? new NotificationSettings
                    {
                        UserId = _currentUserId,
                        NotifyOnNewTickets = true,
                        NotifyOnStatusChanged = true,
                        NotifyOnComments = true,
                        NotifyOnDueSoon = true,
                        DueSoonThresholdMinutes = 60,
                        PlaySound = true,
                        ShowToast = true
                    };

                if (_settings.Id == 0)                 {
                    db.NotificationSettings.Add(_settings);
                    db.SaveChanges();
                }

                                if (NewTicketsCheckBox != null) NewTicketsCheckBox.IsChecked = _settings.NotifyOnNewTickets;
                if (StatusChangedCheckBox != null) StatusChangedCheckBox.IsChecked = _settings.NotifyOnStatusChanged;
                if (CommentsCheckBox != null) CommentsCheckBox.IsChecked = _settings.NotifyOnComments;
                if (DueSoonCheckBox != null) DueSoonCheckBox.IsChecked = _settings.NotifyOnDueSoon;
                if (DueSoonThresholdBox != null) DueSoonThresholdBox.Text = _settings.DueSoonThresholdMinutes.ToString();
                if (SoundCheckBox != null) SoundCheckBox.IsChecked = _settings.PlaySound;
                if (ToastCheckBox != null) ToastCheckBox.IsChecked = _settings.ShowToast;

                                UpdateDueSoonThresholdPanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки настроек: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateDueSoonThresholdPanel()
        {
            if (DueSoonThresholdPanel != null && DueSoonCheckBox != null && DueSoonThresholdBox != null)
            {
                DueSoonThresholdPanel.Opacity = DueSoonCheckBox.IsChecked == true ? 1 : 0.5;
                DueSoonThresholdBox.IsEnabled = DueSoonCheckBox.IsChecked == true;
            }
        }

        private void DueSoonCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateDueSoonThresholdPanel();
        }

        private void DueSoonCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateDueSoonThresholdPanel();
        }

        private void NumberValidation(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
                        if (!int.TryParse(DueSoonThresholdBox?.Text, out int threshold) || threshold < 1)
            {
                MessageBox.Show("Введите корректное значение для порога напоминания (минимум 1 минута)",
                    "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                DueSoonThresholdBox?.Focus();
                return;
            }

            if (threshold > 10080)             {
                MessageBox.Show("Максимальное значение порога — 10080 минут (1 неделя)",
                    "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                DueSoonThresholdBox?.Focus();
                return;
            }

            try
            {
                using var db = new AppDbContext();

                var settings = db.NotificationSettings
                    .FirstOrDefault(s => s.UserId == _currentUserId);

                if (settings == null)
                {
                    settings = new NotificationSettings { UserId = _currentUserId };
                    db.NotificationSettings.Add(settings);
                }

                settings.NotifyOnNewTickets = NewTicketsCheckBox?.IsChecked ?? false;
                settings.NotifyOnStatusChanged = StatusChangedCheckBox?.IsChecked ?? false;
                settings.NotifyOnComments = CommentsCheckBox?.IsChecked ?? false;
                settings.NotifyOnDueSoon = DueSoonCheckBox?.IsChecked ?? false;
                settings.DueSoonThresholdMinutes = threshold;
                settings.PlaySound = SoundCheckBox?.IsChecked ?? false;
                settings.ShowToast = ToastCheckBox?.IsChecked ?? false;

                db.SaveChanges();

                MessageBox.Show("Настройки успешно сохранены", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}