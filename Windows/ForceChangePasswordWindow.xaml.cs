using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ForceChangePasswordWindow : Window
    {
        private readonly AuthService _authService;
        private readonly int _userId;

        public ForceChangePasswordWindow(AuthService authService, int userId)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _userId = userId;
        }

        private void PasswordBox_Changed(object sender, RoutedEventArgs e)
        {
            var pwd = NewPasswordBox.Password;
            var strength = PasswordValidator.GetStrength(pwd);

            var weak = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            var medium = new SolidColorBrush(Color.FromRgb(234, 179, 8));
            var strong = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            var empty = new SolidColorBrush(Color.FromRgb(226, 232, 240));

            switch (strength)
            {
                case PasswordStrength.Weak:
                    StrengthBar1.Background = string.IsNullOrEmpty(pwd) ? empty : weak;
                    StrengthBar2.Background = empty;
                    StrengthBar3.Background = empty;
                    StrengthLabel.Text = string.IsNullOrEmpty(pwd) ? "" : "Слабый";
                    StrengthLabel.Foreground = weak;
                    break;
                case PasswordStrength.Medium:
                    StrengthBar1.Background = medium;
                    StrengthBar2.Background = medium;
                    StrengthBar3.Background = empty;
                    StrengthLabel.Text = "Средний";
                    StrengthLabel.Foreground = medium;
                    break;
                case PasswordStrength.Strong:
                    StrengthBar1.Background = strong;
                    StrengthBar2.Background = strong;
                    StrengthBar3.Background = strong;
                    StrengthLabel.Text = "Надёжный";
                    StrengthLabel.Foreground = strong;
                    break;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            HideError();

            var newPassword = NewPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                ShowError("Введите новый пароль");
                return;
            }

            if (newPassword != confirm)
            {
                ShowError("Пароли не совпадают");
                return;
            }

            var (isValid, errors) = PasswordValidator.Validate(newPassword);
            if (!isValid)
            {
                ShowError("Пароль не соответствует требованиям:\n• " + string.Join("\n• ", errors));
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Content = "Сохранение...";

            if (_authService.ForceChangePassword(_userId, newPassword))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = "Сохранить новый пароль";
                ShowError("Не удалось сохранить пароль. Попробуйте снова.");
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
        }
    }
}
