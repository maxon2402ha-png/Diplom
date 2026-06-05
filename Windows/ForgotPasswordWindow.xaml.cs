using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ForgotPasswordWindow : Window
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;

        public ForgotPasswordWindow(AppDbContext context)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _emailService = EmailService.CreateFromConfig();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();
            var email = EmailBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Введите адрес электронной почты");
                return;
            }

            SendButton.IsEnabled = false;
            SendButton.Content = "Отправка...";

            try
            {
                await SendResetTokenAsync(email);
            }
            finally
            {
                SendButton.IsEnabled = true;
                SendButton.Content = "Отправить токен";
            }
        }

        private async Task SendResetTokenAsync(string email)
        {
#pragma warning disable CA1862
            var user = _context.Users.FirstOrDefault(u =>
                u.Email != null && u.Email.ToLower() == email.ToLower());
#pragma warning restore CA1862

            if (user == null)
            {
                ShowSuccess("Если аккаунт с таким email существует, письмо отправлено.");
                return;
            }

            var existing = _context.PasswordResetTokens
                .Where(t => t.UserId == user.Id && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
                .ToList();
            foreach (var old in existing) old.IsUsed = true;

            var token = Guid.NewGuid().ToString("N")[..8].ToUpper();
            var resetToken = new PasswordResetToken
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            _context.PasswordResetTokens.Add(resetToken);
            _context.SaveChanges();

            var name = user.Username;
            await _emailService.SendPasswordResetAsync(email, name, token);

            ShowSuccess($"Токен отправлен на {email}.\nОн действителен 60 минут.\n\nЕсли почта не настроена — токен: {token}");
        }

        private void EnterToken_Click(object sender, RoutedEventArgs e)
        {
            var resetWnd = new ResetPasswordWindow(_context) { Owner = Owner };
            resetWnd.ShowDialog();
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
            SuccessBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowSuccess(string message)
        {
            SuccessText.Text = message;
            SuccessBorder.Visibility = Visibility.Visible;
            ErrorBorder.Visibility = Visibility.Collapsed;
        }

        private void HideMessages()
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
            SuccessBorder.Visibility = Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
