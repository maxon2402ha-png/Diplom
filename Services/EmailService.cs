using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Serilog;
using КР_Ханников.Core;

namespace КР_Ханников.Services
{
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "Service Desk Platform";
        public bool UseSsl { get; set; } = true;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(Username);
    }

    public class EmailService
    {
        private readonly EmailSettings _settings;
        private static readonly string TemplatesFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "Email");

        public EmailService(EmailSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public static EmailService CreateFromConfig()
        {
            var settings = new EmailSettings
            {
                SmtpHost     = ReadSetting("smtp_host"),
                SmtpPort     = int.TryParse(ReadSetting("smtp_port"), out var p) ? p : 587,
                Username     = ReadSetting("smtp_user"),
                Password     = CryptoHelper.DecryptSensitive(ReadSetting("smtp_pass")) ?? string.Empty,
                FromAddress  = ReadSetting("smtp_from"),
                FromName     = ReadSetting("smtp_from_name") is { Length: > 0 } n ? n : "Service Desk Platform",
                UseSsl       = !string.Equals(ReadSetting("smtp_ssl"), "false", StringComparison.OrdinalIgnoreCase)
            };
            return new EmailService(settings);
        }

        private static string ReadSetting(string key)
        {
            try
            {
                return System.Configuration.ConfigurationManager.AppSettings[key] ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task SendPasswordResetAsync(string toEmail, string toName, string token, CancellationToken ct = default)
        {
            var body = LoadTemplate("password-reset.html")
                .Replace("{Name}", toName)
                .Replace("{Token}", token)
                .Replace("{ExpiresMinutes}", "60");

            await SendAsync(toEmail, toName, "Сброс пароля — Service Desk Platform", body, ct);
        }

        public async Task SendStatusChangedAsync(string toEmail, string toName, int ticketId, string oldStatus, string newStatus, CancellationToken ct = default)
        {
            var body = LoadTemplate("status-changed.html")
                .Replace("{Name}", toName)
                .Replace("{TicketId}", ticketId.ToString())
                .Replace("{OldStatus}", TranslateStatus(oldStatus))
                .Replace("{NewStatus}", TranslateStatus(newStatus));

            await SendAsync(toEmail, toName, $"Статус тикета #{ticketId} изменён", body, ct);
        }

        public async Task SendNewCommentAsync(string toEmail, string toName, int ticketId, string authorName, string commentText, CancellationToken ct = default)
        {
            var body = LoadTemplate("new-comment.html")
                .Replace("{Name}", toName)
                .Replace("{TicketId}", ticketId.ToString())
                .Replace("{Author}", authorName)
                .Replace("{Comment}", System.Web.HttpUtility.HtmlEncode(commentText));

            await SendAsync(toEmail, toName, $"Новый комментарий к тикету #{ticketId}", body, ct);
        }

        public async Task SendDeadlineSoonAsync(string toEmail, string toName, int ticketId, DateTime dueAt, CancellationToken ct = default)
        {
            var body = LoadTemplate("deadline-soon.html")
                .Replace("{Name}", toName)
                .Replace("{TicketId}", ticketId.ToString())
                .Replace("{DueAt}", dueAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));

            await SendAsync(toEmail, toName, $"Скоро дедлайн по тикету #{ticketId}", body, ct);
        }

        private async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct)
        {
            if (!_settings.IsConfigured)
            {
                Log.Warning("[Email] SMTP не настроен, письмо не отправлено: {Subject}", subject);
                return;
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                var secureOption = _settings.UseSsl
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.None;

                await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureOption, ct);
                await client.AuthenticateAsync(_settings.Username, _settings.Password, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);

                Log.Information("[Email] Отправлено: {Subject} → {To}", subject, toEmail);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Email] Ошибка отправки письма: {Subject} → {To}", subject, toEmail);
            }
        }

        private static string LoadTemplate(string fileName)
        {
            var path = Path.Combine(TemplatesFolder, fileName);
            if (File.Exists(path))
                return File.ReadAllText(path);

            return $"<html><body><p>{{{{Body}}}}</p></body></html>";
        }

        private static string TranslateStatus(string status) => status switch
        {
            Constants.TicketStatus.Open       => "Открыт",
            Constants.TicketStatus.InProgress => "В работе",
            Constants.TicketStatus.Resolved   => "Решён",
            Constants.TicketStatus.Closed     => "Закрыт",
            _ => status
        };
    }
}
