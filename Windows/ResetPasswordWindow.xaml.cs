using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ResetPasswordWindow : Window
    {
        private readonly AppDbContext _context;

        public ResetPasswordWindow(AppDbContext context)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void Password_Changed(object sender, RoutedEventArgs e)
        {
            var pwd = NewPasswordBox.Password;
            var strength = PasswordValidator.GetStrength(pwd);

            var weak   = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            var medium = new SolidColorBrush(Color.FromRgb(234, 179, 8));
            var strong = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            var empty  = new SolidColorBrush(Color.FromRgb(226, 232, 240));

            switch (strength)
            {
                case PasswordStrength.Weak:
                    Bar1.Background = string.IsNullOrEmpty(pwd) ? empty : weak;
                    Bar2.Background = empty; Bar3.Background = empty;
                    StrengthLabel.Text = string.IsNullOrEmpty(pwd) ? "" : "Слабый";
                    StrengthLabel.Foreground = weak;
                    break;
                case PasswordStrength.Medium:
                    Bar1.Background = medium; Bar2.Background = medium; Bar3.Background = empty;
                    StrengthLabel.Text = "Средний"; StrengthLabel.Foreground = medium;
                    break;
                case PasswordStrength.Strong:
                    Bar1.Background = strong; Bar2.Background = strong; Bar3.Background = strong;
                    StrengthLabel.Text = "Надёжный"; StrengthLabel.Foreground = strong;
                    break;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            var token = TokenBox.Text.Trim().ToUpper();
            var newPwd = NewPasswordBox.Password;
            var confirm = ConfirmBox.Password;

            if (string.IsNullOrWhiteSpace(token)) { ShowError("Введите токен"); return; }
            if (newPwd != confirm) { ShowError("Пароли не совпадают"); return; }

            var (isValid, errors) = PasswordValidator.Validate(newPwd);
            if (!isValid) { ShowError("Требования к паролю:\n• " + string.Join("\n• ", errors)); return; }

            var resetToken = _context.PasswordResetTokens
                .FirstOrDefault(t => t.Token == token && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);

            if (resetToken == null)
            {
                ShowError("Токен недействителен или истёк срок его действия.");
                return;
            }

            var user = _context.Users.Find(resetToken.UserId);
            if (user == null) { ShowError("Пользователь не найден"); return; }

            user.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(newPwd, Constants.Validation.BcryptWorkFactor);
            user.MustChangePassword = false;
            resetToken.IsUsed = true;
            _context.SaveChanges();

            MessageBox.Show("Пароль успешно изменён! Войдите с новым паролем.",
                "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
