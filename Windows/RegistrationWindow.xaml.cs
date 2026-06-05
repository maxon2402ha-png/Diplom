using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.Versioning;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class RegistrationWindow : Window
    {
        private readonly AppDbContext _context;

        public RegistrationWindow() : this(new AppDbContext()) { }

        public RegistrationWindow(AppDbContext context)
        {
            InitializeComponent();
            _context = context ?? new AppDbContext();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordStrength();
            CheckPasswordMatch();
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            CheckPasswordMatch();
        }

        private void UpdatePasswordStrength()
        {
            if (PasswordBox == null) return;

            var password = PasswordBox.Password;
            int strength = 0;

            if (password.Length >= 6) strength++;
            if (password.Length >= 10) strength++;
            if (Regex.IsMatch(password, @"[A-Z]") && Regex.IsMatch(password, @"[a-z]")) strength++;
            if (Regex.IsMatch(password, @"\d")) strength++;

            var bars = new[] { StrengthBar1, StrengthBar2, StrengthBar3, StrengthBar4 };
            var colors = new[] { "#EF4444", "#F59E0B", "#10B981", "#10B981" };
            var texts = new[] { "Слабый", "Средний", "Хороший", "Отличный" };

            for (int i = 0; i < 4; i++)
            {
                if (bars[i] != null)
                {
                    bars[i].Background = i < strength
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[Math.Min(strength - 1, 2)]))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                }
            }

            if (PasswordStrengthText != null)
            {
                if (password.Length > 0)
                {
                    PasswordStrengthText.Text = $"Надёжность: {texts[Math.Max(0, strength - 1)]}";
                    PasswordStrengthText.Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(colors[Math.Min(Math.Max(0, strength - 1), 2)]));
                    PasswordStrengthText.Visibility = Visibility.Visible;
                }
                else
                {
                    PasswordStrengthText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CheckPasswordMatch()
        {
            if (ConfirmPasswordBox == null || PasswordMatchPanel == null) return;

            if (ConfirmPasswordBox.Password.Length == 0)
            {
                PasswordMatchPanel.Visibility = Visibility.Collapsed;
                return;
            }

            PasswordMatchPanel.Visibility = Visibility.Visible;

            if (PasswordBox.Password == ConfirmPasswordBox.Password)
            {
                if (PasswordMatchIcon != null)
                    PasswordMatchIcon.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                if (PasswordMatchText != null)
                {
                    PasswordMatchText.Text = "Пароли совпадают";
                    PasswordMatchText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
            }
            else
            {
                if (PasswordMatchIcon != null)
                    PasswordMatchIcon.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                if (PasswordMatchText != null)
                {
                    PasswordMatchText.Text = "Пароли не совпадают";
                    PasswordMatchText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
            }
        }

        private void ShowError(string message)
        {
            if (ErrorBorder != null && ErrorText != null)
            {
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorBorder.Visibility = Visibility.Visible;
                ErrorText.Text = message;
            }
        }

        private void HideError()
        {
            if (ErrorBorder != null)
            {
                ErrorPanel.Visibility = Visibility.Collapsed;
                ErrorBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearFieldErrors()
        {
            if (FullNameError != null) FullNameError.Visibility = Visibility.Collapsed;
            if (UsernameError != null) UsernameError.Visibility = Visibility.Collapsed;
            if (PasswordError != null) PasswordError.Visibility = Visibility.Collapsed;
            if (ConfirmPasswordError != null) ConfirmPasswordError.Visibility = Visibility.Collapsed;
        }

        private static void SetFieldError(System.Windows.Controls.TextBlock? label, string? message)
        {
            if (label == null) return;
            if (string.IsNullOrEmpty(message))
            {
                label.Visibility = Visibility.Collapsed;
            }
            else
            {
                label.Text = message;
                label.Visibility = Visibility.Visible;
            }
        }

        private bool ValidateForm()
        {
            HideError();
            ClearFieldErrors();

            bool ok = true;

            if (FullNameBox != null)
            {
                if (string.IsNullOrWhiteSpace(FullNameBox.Text))
                {
                    SetFieldError(FullNameError, "Введите ваше имя");
                    ok = false;
                }
                else if (FullNameBox.Text.Length < 2)
                {
                    SetFieldError(FullNameError, "Минимум 2 символа");
                    ok = false;
                }
            }

            if (UsernameBox != null)
            {
                if (string.IsNullOrWhiteSpace(UsernameBox.Text))
                {
                    SetFieldError(UsernameError, "Введите логин");
                    ok = false;
                }
                else if (UsernameBox.Text.Length < 3)
                {
                    SetFieldError(UsernameError, "Минимум 3 символа");
                    ok = false;
                }
                else if (!Regex.IsMatch(UsernameBox.Text, @"^[a-zA-Z0-9_\.]+$"))
                {
                    SetFieldError(UsernameError, "Только латиница, цифры, «.» и «_»");
                    ok = false;
                }
            }

            if (PasswordBox != null)
            {
                if (string.IsNullOrEmpty(PasswordBox.Password))
                {
                    SetFieldError(PasswordError, "Введите пароль");
                    ok = false;
                }
                else if (PasswordBox.Password.Length < 6)
                {
                    SetFieldError(PasswordError, "Минимум 6 символов");
                    ok = false;
                }
            }

            if (PasswordBox != null && ConfirmPasswordBox != null
                && PasswordBox.Password != ConfirmPasswordBox.Password)
            {
                SetFieldError(ConfirmPasswordError, "Пароли не совпадают");
                ok = false;
            }

            return ok;
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm())
                return;

            if (RegisterButton != null)
            {
                RegisterButton.IsEnabled = false;
                RegisterButton.Content = "Обработка...";
            }

            try
            {
                                var username = UsernameBox?.Text?.Trim() ?? "";
                var normalizedUsername = username.ToLower();

                if (_context.Users.Any(u => u.Username.ToLower() == normalizedUsername))
                {
                    SetFieldError(UsernameError, "Логин уже занят");
                    UsernameBox?.Focus();
                    UsernameBox?.Focus();
                    return;
                }

                var user = new User
                {
                    Username = username,
                    Email = "",
                    PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(PasswordBox?.Password ?? "", 13),
                    Role = Constants.UserRoles.Client,
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = true
                };

                _context.Users.Add(user);
                _context.SaveChanges();

                var client = new Client
                {
                    Name = FullNameBox?.Text?.Trim() ?? "User",
                    UserId = user.Id,
                    Email = ""
                };

                _context.Clients.Add(client);
                _context.SaveChanges();

                _context.AuditLogs.Add(new AuditLog
                {
                    Username = username,
                    Action = "Register",
                    Details = "Регистрация нового клиента",
                    Timestamp = DateTime.UtcNow
                });
                _context.SaveChanges();

                MessageBox.Show(
                    "Регистрация успешно завершена!\nТеперь вы можете войти в систему.",
                    "Успех",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка регистрации: {ex.Message}");
            }
            finally
            {
                if (RegisterButton != null)
                {
                    RegisterButton.IsEnabled = true;
                    RegisterButton.Content = "Зарегистрироваться";
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}