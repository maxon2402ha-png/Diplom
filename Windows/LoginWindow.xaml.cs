using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Helpers;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class LoginWindow : Window
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;

        public LoginWindow()
        {
            InitializeComponent();
            _context = App.CreateDbContext();
            _authService = new AuthService(_context);
            LoadSavedCredentials();
        }

        public LoginWindow(AppDbContext context, AuthService authService)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            LoadSavedCredentials();
        }

        private void LoadSavedCredentials()
        {
            try
            {
                if (Properties.Settings.Default.IsRemembered)
                {
                    UsernameBox.Text = Properties.Settings.Default.Username;
                    RememberMeCheck.IsChecked = true;
                    Loaded += (s, e) => PasswordBox.Focus();
                }
                else
                {
                    Loaded += (s, e) => UsernameBox.Focus();
                }
            }
            catch { }
        }

        private void SaveCredentials(string username)
        {
            try
            {
                if (RememberMeCheck.IsChecked == true)
                {
                    Properties.Settings.Default.Username = username;
                    Properties.Settings.Default.IsRemembered = true;
                }
                else
                {
                    Properties.Settings.Default.Username = string.Empty;
                    Properties.Settings.Default.IsRemembered = false;
                }
                Properties.Settings.Default.Save();
            }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TryBeginAnimation("WindowLoadAnimation", this);
            TryBeginAnimation("BrandPanelAnimation", FindName("BrandPanel") as FrameworkElement);
            TryBeginAnimation("LoginCardAnimation", FindName("LoginContainer") as FrameworkElement);
        }

        private void TryBeginAnimation(string resourceKey, FrameworkElement? target = null)
        {
            try
            {
                if (this.Resources.Contains(resourceKey))
                {
                    var anim = this.FindResource(resourceKey) as Storyboard;
                    if (anim != null)
                    {
                        if (target != null) anim.Begin(target);
                        else anim.Begin();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoginWindow] Animation error: {ex.Message}");
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            HideError();
            UsernameError.Visibility = Visibility.Collapsed;
            PasswordError.Visibility = Visibility.Collapsed;

            var username = UsernameBox.Text;
            var password = PasswordBox.Password;

            bool hasError = false;
            if (string.IsNullOrWhiteSpace(username))
            {
                UsernameError.Text = "Введите логин";
                UsernameError.Visibility = Visibility.Visible;
                hasError = true;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                PasswordError.Text = "Введите пароль";
                PasswordError.Visibility = Visibility.Visible;
                hasError = true;
            }
            if (hasError) return;

            LoginButton.IsEnabled = false;
            LoginButton.Content = "Вход...";

            try
            {
                var result = _authService.Login(username, password);

                switch (result.Status)
                {
                    case LoginStatus.Success:
                        HandleSuccessfulLogin(username, result.User!);
                        break;

                    case LoginStatus.Locked:
                        var remaining = result.LockoutRemaining!.Value;
                        var timeStr = remaining.TotalMinutes >= 1
                            ? $"{(int)remaining.TotalMinutes} мин. {remaining.Seconds} сек."
                            : $"{remaining.Seconds} сек.";
                        ShowError($"Аккаунт временно заблокирован.\nПовторите через {timeStr}.");
                        PasswordBox.Clear();
                        break;

                    case LoginStatus.EmailNotVerified:
                        ShowError("Ваш Email не подтверждён. Завершите регистрацию.");
                        break;

                    case LoginStatus.InvalidCredentials:
                        if (result.AttemptsLeft.HasValue)
                            ShowError($"Неверный логин или пароль. Осталось попыток: {result.AttemptsLeft}");
                        else
                            ShowError("Неверный логин или пароль");
                        PasswordBox.Clear();
                        PasswordBox.Focus();
                        break;

                    case LoginStatus.Error:
                        ShowError($"Ошибка: {result.ErrorMessage}");
                        break;
                }
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Войти";
            }
        }

        private void HandleSuccessfulLogin(string username, User user)
        {
            SaveCredentials(username);
            ApplyUserTheme(user.Id);

            if (user.MustChangePassword)
            {
                var changeWnd = new ForceChangePasswordWindow(_authService, user.Id)
                {
                    Owner = this
                };
                if (changeWnd.ShowDialog() != true)
                {
                    ShowError("Вход невозможен без смены пароля");
                    _authService.Logout();
                    return;
                }
            }

            var mainWindow = new MainWindow(_context, _authService);
            mainWindow.Show();
            this.Close();
        }

        private void ShowError(string message)
        {
            if (FindName("ErrorText") is System.Windows.Controls.TextBlock tb)
                tb.Text = message;
            if (FindName("ErrorContainer") is FrameworkElement container)
                container.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            if (FindName("ErrorContainer") is FrameworkElement container)
                container.Visibility = Visibility.Collapsed;
        }

        private void ApplyUserTheme(int userId)
        {
            try
            {
                using var db = App.CreateDbContext();
                var settings = db.UserUiSettings.FirstOrDefault(s => s.UserId == userId);
                bool isDark = settings != null && settings.Theme == "Dark";
                ThemeManager.ApplyTheme(isDark);
            }
            catch { }
        }

        private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wnd = new ForgotPasswordWindow(_context) { Owner = this };
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка: {ex.Message}");
            }
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var regWindow = new RegistrationWindow(_context)
                {
                    Owner = this
                };

                if (regWindow.ShowDialog() == true)
                {
                    ShowError("Регистрация успешна! Проверьте почту и войдите в систему.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка открытия регистрации: {ex.Message}");
            }
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Login_Click(sender, e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
