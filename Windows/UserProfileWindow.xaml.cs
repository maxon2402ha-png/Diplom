using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Helpers;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class UserProfileWindow : Window
    {
        private readonly AuthService _authService;
        private readonly int _userId;
        private User _currentUser = null!;
        private string _tempAvatarPath = string.Empty;

        public UserProfileWindow(AuthService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _userId = _authService.CurrentUser!.Id;

            Loaded += (s, e) => LoadUserData();
        }

                private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadUserData()
        {
            try
            {
                using var db = App.CreateDbContext();

                var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == _userId);

                if (user == null)
                {
                    MessageBox.Show("Пользователь не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                _currentUser = user;

                var employee = db.Employees.AsNoTracking().FirstOrDefault(e => e.UserId == _userId);
                var client = db.Clients.AsNoTracking().FirstOrDefault(c => c.UserId == _userId);

                var displayName = employee?.Name ?? client?.Name ?? _currentUser.Username;

                if (DisplayNameText != null) DisplayNameText.Text = displayName;
                if (UsernameText != null) UsernameText.Text = $"@{_currentUser.Username}";
                if (UsernameBox != null) UsernameBox.Text = _currentUser.Username;

                if (FullNameBox != null) FullNameBox.Text = displayName;
                if (EmailBox != null) EmailBox.Text = _currentUser.Email ?? client?.Email;

                if (RoleText != null)
                {
                    RoleText.Text = _currentUser.Role switch
                    {
                        Constants.UserRoles.Admin => "Администратор",
                        Constants.UserRoles.Support => "Поддержка",
                        Constants.UserRoles.Client => "Клиент",
                        _ => _currentUser.Role
                    };
                }

                if (CreatedAtText != null) CreatedAtText.Text = _currentUser.CreatedAt.ToString("dd.MM.yyyy");
                if (LastLoginText != null) LastLoginText.Text = _currentUser.LastLoginAt?.ToString("dd.MM.yyyy HH:mm") ?? "Только что";

                UpdateAvatarDisplay(_currentUser.AvatarPath);

                                if (UserStatsPanel != null && StatLabel1 != null && StatValue1 != null && StatLabel2 != null && StatValue2 != null)
                {
                    UserStatsPanel.Visibility = Visibility.Visible;

                    if (_currentUser.Role == Constants.UserRoles.Client && client != null)
                    {
                        var totalTickets = db.Tickets.Count(t => t.ClientId == client.Id);
                        var activeTickets = db.Tickets.Count(t => t.ClientId == client.Id && t.Status != Constants.TicketStatus.Closed);

                        StatLabel1.Text = "Всего обращений";
                        StatValue1.Text = totalTickets.ToString();

                        StatLabel2.Text = "В работе";
                        StatValue2.Text = activeTickets.ToString();
                    }
                    else if (_currentUser.Role == Constants.UserRoles.Support && employee != null)
                    {
                        var closedTickets = db.Tickets.Count(t => t.AssigneeEmployeeId == employee.Id && t.Status == Constants.TicketStatus.Closed);

                        // Реальный средний рейтинг по отзывам клиентов; «—», если оценок ещё нет.
                        var ratings = db.Feedbacks
                            .Where(f => f.SupportId == employee.Id)
                            .Select(f => f.Rating)
                            .ToList();

                        StatLabel1.Text = "Рейтинг";
                        StatValue1.Text = ratings.Count > 0 ? $"{ratings.Average():0.0}" : "—";
                        StatLabel2.Text = "Закрыто тикетов";
                        StatValue2.Text = closedTickets.ToString();
                    }
                    else if (_currentUser.Role == Constants.UserRoles.Admin)
                    {
                        var totalSystemTickets = db.Tickets.Count();
                        var totalEmployees = db.Employees.Count();

                        StatLabel1.Text = "Всего заявок";
                        StatValue1.Text = totalSystemTickets.ToString();

                        StatLabel2.Text = "Сотрудников";
                        StatValue2.Text = totalEmployees.ToString();
                    }
                    else
                    {
                        UserStatsPanel.Visibility = Visibility.Collapsed;
                    }
                }

                                var settings = db.UserUiSettings.AsNoTracking().FirstOrDefault(s => s.UserId == _userId);
                if (ThemeToggle != null)
                {
                    ThemeToggle.IsChecked = settings?.Theme == "Dark";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки профиля: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isDark = ThemeToggle.IsChecked == true;
                ThemeManager.ApplyTheme(isDark);

                using var db = App.CreateDbContext();
                var settings = db.UserUiSettings.FirstOrDefault(s => s.UserId == _userId);

                if (settings == null)
                {
                    settings = new UserUiSettings { UserId = _userId };
                    db.UserUiSettings.Add(settings);
                }

                settings.Theme = isDark ? "Dark" : "Light";
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения темы: {ex.Message}");
            }
        }

        private void UpdateAvatarDisplay(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (ProfileImage != null) ProfileImage.ImageSource = null;
                if (DefaultAvatarIcon != null) DefaultAvatarIcon.Visibility = Visibility.Visible;
            }
            else
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;                     bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();

                    if (ProfileImage != null) ProfileImage.ImageSource = bitmap;
                    if (DefaultAvatarIcon != null) DefaultAvatarIcon.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    if (ProfileImage != null) ProfileImage.ImageSource = null;
                    if (DefaultAvatarIcon != null) DefaultAvatarIcon.Visibility = Visibility.Visible;
                }
            }
        }

        private void ChangePhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Выберите аватар"
            };

            if (dialog.ShowDialog() == true)
            {
                _tempAvatarPath = dialog.FileName;
                UpdateAvatarDisplay(_tempAvatarPath);
            }
        }

                private void NewPassBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordStrength();
            CheckPasswordMatch();
            UpdateChangePasswordButton();
        }

        private void ConfirmPassBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            CheckPasswordMatch();
            UpdateChangePasswordButton();
        }

        private void UpdatePasswordStrength()
        {
            if (NewPassBox == null) return;

            var password = NewPassBox.Password;
            int strength = 0;

            if (password.Length >= 6) strength++;
            if (password.Length >= 10) strength++;
            if (Regex.IsMatch(password, @"[A-Z]") && Regex.IsMatch(password, @"[a-z]")) strength++;
            if (Regex.IsMatch(password, @"\d") || Regex.IsMatch(password, @"\W")) strength++;

            var gray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
            var red = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            var orange = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            var green = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

            if (StrengthBar1 != null) StrengthBar1.Background = strength > 0 ? red : gray;
            if (StrengthBar2 != null) StrengthBar2.Background = strength > 1 ? orange : gray;
            if (StrengthBar3 != null) StrengthBar3.Background = strength > 2 ? green : gray;
            if (StrengthBar4 != null) StrengthBar4.Background = strength > 3 ? green : gray;
        }

        private void CheckPasswordMatch()
        {
            if (ConfirmPassBox == null || PasswordMatchPanel == null || NewPassBox == null) return;

            if (ConfirmPassBox.Password.Length == 0 && NewPassBox.Password.Length == 0)
            {
                PasswordMatchPanel.Visibility = Visibility.Collapsed;
                return;
            }

            PasswordMatchPanel.Visibility = Visibility.Visible;

            bool isMatch = NewPassBox.Password == ConfirmPassBox.Password && NewPassBox.Password.Length >= 6;
            string colorHex = isMatch ? "#10B981" : "#EF4444";             var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

            if (PasswordMatchIcon != null) PasswordMatchIcon.Background = brush;

            if (PasswordMatchText != null)
            {
                if (NewPassBox.Password != ConfirmPassBox.Password)
                    PasswordMatchText.Text = "Пароли не совпадают";
                else if (NewPassBox.Password.Length < 6)
                    PasswordMatchText.Text = "Пароль слишком короткий";
                else
                    PasswordMatchText.Text = "Пароли совпадают";

                PasswordMatchText.Foreground = brush;
            }
        }

        private void UpdateChangePasswordButton()
        {
            if (ChangePasswordButton == null || OldPassBox == null || NewPassBox == null || ConfirmPassBox == null) return;

            ChangePasswordButton.IsEnabled =
                OldPassBox.Password.Length > 0 &&
                NewPassBox.Password.Length >= 6 &&
                NewPassBox.Password == ConfirmPassBox.Password;
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var db = App.CreateDbContext();
                var user = db.Users.Find(_userId);

                if (user == null) return;

                if (!CryptoHelper.VerifyPassword(OldPassBox.Password, user.PasswordHash))
                {
                    MessageBox.Show("Неверный текущий пароль", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    OldPassBox.Clear();
                    return;
                }

                user.PasswordHash = CryptoHelper.HashPassword(NewPassBox.Password);

                db.AuditLogs.Add(new AuditLog
                {
                    Username = user.Username,
                    Action = "Смена пароля",
                    Details = "Пользователь успешно обновил пароль.",
                    Timestamp = DateTime.UtcNow
                });

                db.SaveChanges();

                MessageBox.Show("Пароль успешно изменён", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                OldPassBox.Clear();
                NewPassBox.Clear();
                ConfirmPassBox?.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text) || string.IsNullOrWhiteSpace(UsernameBox.Text))
            {
                MessageBox.Show("Имя и Логин не могут быть пустыми.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var db = App.CreateDbContext();
                var userToUpdate = db.Users.Find(_userId);

                if (userToUpdate == null)
                {
                    MessageBox.Show("Пользователь не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                                if (!string.IsNullOrEmpty(_tempAvatarPath))
                {
                    var avatarsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "КР_Ханников", "Avatars");
                    if (!Directory.Exists(avatarsDir)) Directory.CreateDirectory(avatarsDir);

                    var newFileName = $"avatar_{_userId}_{DateTime.Now.Ticks}{Path.GetExtension(_tempAvatarPath)}";
                    var destPath = Path.Combine(avatarsDir, newFileName);

                    File.Copy(_tempAvatarPath, destPath, true);
                    userToUpdate.AvatarPath = destPath;
                }

                                var newUsername = UsernameBox.Text.Trim();
                if (newUsername != userToUpdate.Username)
                {
                    if (db.Users.Any(u => u.Username.ToLower() == newUsername.ToLower() && u.Id != _userId))
                    {
                        MessageBox.Show("Такой логин уже занят другим пользователем", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    userToUpdate.Username = newUsername;
                }

                userToUpdate.Email = EmailBox?.Text.Trim();

                                var newName = FullNameBox.Text.Trim();
                if (userToUpdate.Role == Constants.UserRoles.Client)
                {
                    var client = db.Clients.FirstOrDefault(c => c.UserId == _userId);
                    if (client != null)
                    {
                        client.Name = newName;
                        client.Email = EmailBox?.Text.Trim() ?? "";
                    }
                }
                else
                {
                    var emp = db.Employees.FirstOrDefault(e => e.UserId == _userId);
                    if (emp != null) emp.Name = newName;
                }

                                db.AuditLogs.Add(new AuditLog
                {
                    Username = userToUpdate.Username,
                    Action = "Обновление профиля",
                    Details = "Данные профиля обновлены",
                    Timestamp = DateTime.UtcNow
                });

                db.SaveChanges();

                                if (_authService.CurrentUser != null)
                {
                    _authService.CurrentUser.AvatarPath = userToUpdate.AvatarPath;
                    _authService.CurrentUser.Username = userToUpdate.Username;
                    _authService.CurrentUser.Email = userToUpdate.Email;
                }

                MessageBox.Show("Изменения успешно сохранены!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}