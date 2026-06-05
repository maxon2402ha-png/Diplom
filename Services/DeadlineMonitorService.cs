using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Serilog;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Services
{
    public sealed class DeadlineMonitorService : IDisposable
    {
        private Timer? _timer;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DeadlineWarningThreshold = TimeSpan.FromHours(1);
        private static readonly TimeSpan AutoCloseResolvedAfter = TimeSpan.FromHours(72);
        private bool _disposed;

        public void Start()
        {
            _timer = new Timer(_ => RunCheck(), null, TimeSpan.FromSeconds(30), CheckInterval);
            Log.Information("[DeadlineMonitor] Сервис запущен, интервал {Minutes} мин.", CheckInterval.TotalMinutes);
        }

        private void RunCheck()
        {
            try
            {
                using var db = App.CreateDbContext();
                CheckDeadlines(db);
                AutoCloseResolvedTickets(db);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DeadlineMonitor] Ошибка при проверке дедлайнов");
            }
        }

        private static void CheckDeadlines(AppDbContext db)
        {
            var now = DateTime.UtcNow;
            var warningThreshold = now.Add(DeadlineWarningThreshold);

            var tickets = db.Tickets
                .Include(t => t.Assignee).ThenInclude(a => a!.User)
                .Where(t =>
                    t.DueAt.HasValue &&
                    t.Status != Constants.TicketStatus.Closed &&
                    t.Status != Constants.TicketStatus.Resolved)
                .ToList();

            foreach (var ticket in tickets)
            {
                bool overdue = ticket.DueAt!.Value < now;
                bool dueSoon = !overdue && ticket.DueAt.Value <= warningThreshold;

                if (overdue && !ticket.IsOverdue)
                {
                    ticket.IsOverdue = true;
                    db.SaveChanges();

                    NotifyAdminsAboutOverdue(db, ticket);
                    Log.Warning("[DeadlineMonitor] Тикет #{Id} просрочен (DueAt={Due})", ticket.Id, ticket.DueAt);
                }
                else if (dueSoon && !ticket.NotifiedAboutDeadline)
                {
                    ticket.NotifiedAboutDeadline = true;
                    db.SaveChanges();

                    NotifyOperatorAboutDeadline(db, ticket);
                    Log.Information("[DeadlineMonitor] Тикет #{Id} истекает через < 1 ч", ticket.Id);
                }
            }
        }

        private static void AutoCloseResolvedTickets(AppDbContext db)
        {
            var now = DateTime.UtcNow;
            var autoCloseDeadline = now.Subtract(AutoCloseResolvedAfter);

            var resolvedTickets = db.Tickets
                .Include(t => t.Client)
                .Where(t =>
                    t.Status == Constants.TicketStatus.Resolved &&
                    t.ClientConfirmationDeadline.HasValue &&
                    t.ClientConfirmationDeadline.Value <= now)
                .ToList();

            foreach (var ticket in resolvedTickets)
            {
                ticket.Status = Constants.TicketStatus.Closed;
                ticket.ClosedAt = now;

                db.TicketHistories.Add(new TicketHistory
                {
                    TicketId = ticket.Id,
                    Action = "Авто-закрытие",
                    Details = "Тикет автоматически закрыт после 72 часов без подтверждения клиента.",
                    Timestamp = now
                });

                db.SaveChanges();

                NotifyClientAboutAutoClosed(db, ticket);
                Log.Information("[DeadlineMonitor] Тикет #{Id} авто-закрыт (без ответа клиента 72ч)", ticket.Id);
            }
        }

        private static void NotifyAdminsAboutOverdue(AppDbContext db, Ticket ticket)
        {
            var admins = db.Users.Where(u => u.Role == Constants.UserRoles.Admin).ToList();
            foreach (var admin in admins)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = admin.Id,
                    Title = "Тикет просрочен",
                    Message = $"Тикет #{ticket.Id} «{ticket.Title}» просрочен. Дедлайн: {ticket.DueAt:dd.MM HH:mm}",
                    Type = Constants.NotificationTypes.TicketOverdue,
                    TicketId = ticket.Id,
                    CreatedAt = DateTime.UtcNow
                });
            }
            db.SaveChanges();
        }

        private static void NotifyOperatorAboutDeadline(AppDbContext db, Ticket ticket)
        {
            if (!ticket.AssigneeEmployeeId.HasValue) return;

            var emp = db.Employees.FirstOrDefault(e => e.Id == ticket.AssigneeEmployeeId);
            if (emp == null) return;

            db.Notifications.Add(new Notification
            {
                UserId = emp.UserId,
                Title = "Скоро дедлайн",
                Message = $"Тикет #{ticket.Id} «{ticket.Title}» нужно завершить до {ticket.DueAt:HH:mm}",
                Type = Constants.NotificationTypes.TicketDueSoon,
                TicketId = ticket.Id,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private static void NotifyClientAboutAutoClosed(AppDbContext db, Ticket ticket)
        {
            if (ticket.Client == null) return;

            db.Notifications.Add(new Notification
            {
                UserId = ticket.Client.UserId,
                Title = "Тикет закрыт",
                Message = $"Тикет #{ticket.Id} «{ticket.Title}» автоматически закрыт через 72 часа после решения.",
                Type = Constants.NotificationTypes.TicketClosed,
                TicketId = ticket.Id,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer?.Dispose();
                _disposed = true;
            }
        }
    }
}
