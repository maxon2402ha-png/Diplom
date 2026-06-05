using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class NotificationCenterWindow : Window
    {
        private readonly int _currentUserId;
        private List<Notification> _allNotifications = new();
        private string _currentFilter = "all";

        public NotificationCenterWindow(int userId)
        {
            InitializeComponent();
            _currentUserId = userId;
            LoadNotifications();
        }

        private void LoadNotifications()
        {
            try
            {
                using var db = App.CreateDbContext();

                _allNotifications = db.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == _currentUserId)
                    .OrderByDescending(n => n.CreatedAt)
                    .ToList();

                ApplyFilter();
                UpdateCounters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки уведомлений: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            if (_allNotifications == null) return;

            var filtered = _allNotifications.AsEnumerable();

            switch (_currentFilter)
            {
                case "unread":
                    filtered = filtered.Where(n => !n.IsRead);
                    break;
                case "read":
                    filtered = filtered.Where(n => n.IsRead);
                    break;
            }

            var list = filtered.ToList();

            if (NotificationsGrid != null)
            {
                NotificationsGrid.ItemsSource = list;
                NotificationsGrid.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (EmptyState != null)
            {
                EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateCounters()
        {
            if (_allNotifications == null) return;

            var total = _allNotifications.Count;
            var unread = _allNotifications.Count(n => !n.IsRead);

            if (TotalCountText != null)
                TotalCountText.Text = $"{total} {GetNotificationSuffix(total)}";

            if (UnreadBadge != null && UnreadCountText != null)
            {
                if (unread > 0)
                {
                    UnreadBadge.Visibility = Visibility.Visible;
                    UnreadCountText.Text = unread.ToString();
                }
                else
                {
                    UnreadBadge.Visibility = Visibility.Collapsed;
                }
            }
        }

        private string GetNotificationSuffix(int count)
        {
            var lastTwo = count % 100;
            var lastOne = count % 10;

            if (lastTwo >= 11 && lastTwo <= 19)
                return "уведомлений";

            return lastOne switch
            {
                1 => "уведомление",
                2 or 3 or 4 => "уведомления",
                _ => "уведомлений"
            };
        }

        private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilterCombo == null || FilterCombo.SelectedIndex < 0) return;

            _currentFilter = FilterCombo.SelectedIndex switch
            {
                0 => "all",
                1 => "unread",
                2 => "read",
                _ => "all"
            };

            ApplyFilter();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadNotifications();
        }

        private void NotificationsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedTicket();
        }

        private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allNotifications == null) return;

            var unreadCount = _allNotifications.Count(n => !n.IsRead);
            if (unreadCount == 0)
            {
                MessageBox.Show("Нет непрочитанных уведомлений", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                using var db = App.CreateDbContext();

                var unreadNotifications = db.Notifications
                    .Where(n => n.UserId == _currentUserId && !n.IsRead)
                    .ToList();

                foreach (var notification in unreadNotifications)
                {
                    notification.IsRead = true;
                }

                db.SaveChanges();
                LoadNotifications();

                MessageBox.Show($"Отмечено прочитанными: {unreadCount}", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSelectedTicket()
        {
            if (NotificationsGrid.SelectedItem is not Notification notification)
                return;

            if (notification.TicketId == null)
            {
                MessageBox.Show("Это уведомление не связано с тикетом", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!notification.IsRead)
                {
                    using var db = App.CreateDbContext();
                    var dbNotification = db.Notifications.Find(notification.Id);
                    if (dbNotification != null)
                    {
                        dbNotification.IsRead = true;
                        db.SaveChanges();
                    }
                }

                var detailsWindow = new TicketDetailsWindow(notification.TicketId.Value);
                detailsWindow.Owner = this;
                detailsWindow.ShowDialog();

                LoadNotifications();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия тикета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}