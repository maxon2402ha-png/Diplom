using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Windows;

namespace КР_Ханников.Services
{
    [SupportedOSPlatform("windows")]
    public class NotificationService
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;

        public NotificationService(AppDbContext context, AuthService authService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        private void CreateNotification(
            int userId,
            string title,
            string message,
            string type,
            int? ticketId = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                TicketId = ticketId,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.Notifications.Add(notification);
            _context.SaveChanges();

            var current = _authService.CurrentUser;
            if (current != null && current.Id == userId)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                                                string toastType = "Info";

                        if (type == "Overdue" || type == "Error" || type == "SlaRiskAlert")
                        {
                            toastType = "Error";
                        }
                        else if (type == "DueSoon" || type == "WorkloadAlert")
                        {
                            toastType = "Warning";
                        }
                        else if (type == "StatusChanged" && message.Contains("Closed"))
                        {
                            toastType = "Success";
                        }

                                                                        ToastWindow.Show(title, message, toastType, ticketId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ошибка показа Toast: {ex.Message}");
                    }
                });
            }
        }

        private NotificationSettings GetSettings(int userId)
        {
            var settings = _context.NotificationSettings.FirstOrDefault(s => s.UserId == userId);
            if (settings == null)
            {
                settings = new NotificationSettings { UserId = userId };
                _context.NotificationSettings.Add(settings);
                _context.SaveChanges();
            }
            return settings;
        }

        
                public void NotifyOperatorsAboutNewTicket(Ticket ticket)
        {
            var operators = _context.Users
                .Where(u => u.Role == Constants.UserRoles.Support || u.Role == Constants.UserRoles.Admin)
                .ToList();

            foreach (var op in operators)
            {
                if (GetSettings(op.Id).NotifyOnNewTickets)
                {
                    CreateNotification(op.Id, "Новый тикет",
                        $"#{ticket.Id}: {ticket.Title}", "NewTicket", ticket.Id);
                }
            }
        }

                public void NotifyStatusChanged(Ticket ticket, string oldStatus, string newStatus)
        {
            if (oldStatus == newStatus) return;

            var msg = $"Статус тикета #{ticket.Id} изменён: {oldStatus} -> {newStatus}";

                        var clientUser = _context.Clients.AsNoTracking().FirstOrDefault(c => c.Id == ticket.ClientId);
            if (clientUser != null && GetSettings(clientUser.UserId).NotifyOnStatusChanged)
            {
                CreateNotification(clientUser.UserId, "Обновление статуса", msg, "StatusChanged", ticket.Id);
            }

                        if (ticket.AssigneeEmployeeId.HasValue)
            {
                var emp = _context.Employees.AsNoTracking().FirstOrDefault(e => e.Id == ticket.AssigneeEmployeeId);
                if (emp != null && GetSettings(emp.UserId).NotifyOnStatusChanged)
                {
                    CreateNotification(emp.UserId, "Обновление статуса", msg, "StatusChanged", ticket.Id);
                }
            }
        }

                public void NotifyDueDateChanged(Ticket ticket, DateTime? oldDue, DateTime? newDue)
        {
            if (!newDue.HasValue || !ticket.AssigneeEmployeeId.HasValue) return;

            var emp = _context.Employees.FirstOrDefault(e => e.Id == ticket.AssigneeEmployeeId);
            if (emp != null && GetSettings(emp.UserId).NotifyOnDueSoon)
            {
                CreateNotification(emp.UserId, "Срок изменен",
                    $"Новый дедлайн по тикету #{ticket.Id}: {newDue:dd.MM HH:mm}", "Info", ticket.Id);
            }
        }

                public void CheckDueSoonTicketsForCurrentUser()
        {
            var current = _authService.CurrentUser;
            if (current == null) return;

            var settings = GetSettings(current.Id);
            if (!settings.NotifyOnDueSoon) return;

            var now = DateTime.UtcNow;
            var threshold = now.AddMinutes(settings.DueSoonThresholdMinutes);

                        var ticketsQuery = _context.Tickets.AsNoTracking().Where(t => t.Status != Constants.TicketStatus.Closed && t.DueAt.HasValue);

            if (current.Role == Constants.UserRoles.Support || current.Role == Constants.UserRoles.Admin)
            {
                                var emp = _context.Employees.FirstOrDefault(e => e.UserId == current.Id);
                if (emp == null) return;
                ticketsQuery = ticketsQuery.Where(t => t.AssigneeEmployeeId == emp.Id);
            }
            else
            {
                                var client = _context.Clients.FirstOrDefault(c => c.UserId == current.Id);
                if (client == null) return;
                ticketsQuery = ticketsQuery.Where(t => t.ClientId == client.Id);
            }

            var tickets = ticketsQuery.Where(t => t.DueAt <= threshold).ToList();

            foreach (var t in tickets)
            {
                bool alreadyNotified = _context.Notifications.Any(n =>
                    n.UserId == current.Id && n.TicketId == t.Id &&
                    (n.Type == "DueSoon" || n.Type == "Overdue") &&
                    !n.IsRead);

                if (!alreadyNotified)
                {
                    if (t.DueAt < now)
                    {
                        CreateNotification(current.Id, "ПРОСРОЧЕНО",
                            $"Срок выполнения тикета #{t.Id} истек!", "Overdue", t.Id);
                    }
                    else
                    {
                        CreateNotification(current.Id, "Срок истекает",
                            $"Тикет #{t.Id} нужно закрыть до {t.DueAt:HH:mm}", "DueSoon", t.Id);
                    }
                }
            }
        }

                                        public async Task CheckWorkloadAlertsForCurrentUserAsync()
        {
            var current = _authService.CurrentUser;
            if (current == null) return;

                        if (current.Role != Constants.UserRoles.Admin) return;

            try
            {
                                                                using var db = App.CreateDbContext();

                                var workloadService = new WorkloadService(db);
                var summary = await workloadService.GetSummaryAsync();

                if (summary.OverloadedCount > 0)
                {
                                                            bool alreadyNotified = _context.Notifications.Any(n =>
                        n.UserId == current.Id
                        && n.Type == "WorkloadAlert"
                        && !n.IsRead);

                    if (!alreadyNotified)
                    {
                        CreateNotification(current.Id, "Перегрузка операторов",
                            $"Перегружено операторов: {summary.OverloadedCount}. " +
                            "Рекомендуется перераспределить нагрузку.",
                            "WorkloadAlert");
                    }
                }

                                var forecastService = new ForecastService(db);
                var risk = await forecastService.AssessSlaRiskAsync(7);

                if (risk.HasEnoughData && risk.RiskLevel == SlaRiskLevel.High)
                {
                    bool slaNotified = _context.Notifications.Any(n =>
                        n.UserId == current.Id
                        && n.Type == "SlaRiskAlert"
                        && !n.IsRead);

                    if (!slaNotified)
                    {
                        CreateNotification(current.Id, "Риск срыва SLA",
                            "Прогнозируемая нагрузка превышает возможности команды " +
                            "на ближайшую неделю.",
                            "SlaRiskAlert");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorkloadAlerts] {ex.Message}");
            }
        }

                public void ShowUnreadNotificationsForCurrentUser()
        {
            var current = _authService.CurrentUser;
            if (current == null) return;

            var unread = _context.Notifications
                .Where(n => n.UserId == current.Id && !n.IsRead)
                .OrderBy(n => n.CreatedAt)
                .ToList();

            foreach (var n in unread.Take(3))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        ToastWindow.Show(n.Title, n.Message, "Info", n.TicketId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ошибка показа Toast: {ex.Message}");
                    }
                });
            }
        }
    }
}