using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Services
{
    /// <summary>
    /// Сервис расчёта ключевых показателей эффективности (KPI) технической поддержки.
    /// Централизует всю аналитику по тикетам и сотрудникам в одном месте.
    ///
    /// Реализуемые метрики (методология ITIL / Service Desk):
    ///  - First Response Time (FRT) — время первого ответа клиенту;
    ///  - Resolution Time / ART — среднее время разрешения тикета;
    ///  - SLA Compliance — доля тикетов, закрытых в установленный срок (DueAt);
    ///  - Overdue Rate — доля просроченных тикетов;
    ///  - Reopen Rate — доля переоткрытых тикетов;
    ///  - Throughput — количество закрытых тикетов (пропускная способность);
    ///  - Workload Index — индекс текущей нагрузки сотрудника.
    /// </summary>
    public class KpiService
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Максимальное значение индекса нагрузки сотрудника (для шкалы ProgressBar).
        /// Соответствует Maximum="20" у индикатора WorkloadProgress.
        /// При весах приоритетов это эквивалент примерно 10 обычных тикетов.
        /// </summary>
        public const int MaxWorkloadPoints = 20;

        public KpiService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // Статусы, которые считаются "тикет решён" (учитываем и Closed, и Resolved).
        private static readonly string[] ClosedLikeStatuses =
        {
            Constants.TicketStatus.Closed,
            Constants.TicketStatus.Resolved
        };

        // ==================================================================
        //  МЕТОДЫ ДЛЯ ОТДЕЛЬНОГО СОТРУДНИКА
        //  (используются в AgentDashboardControl — персональном дашборде агента)
        // ==================================================================

        /// <summary>
        /// Рассчитывает индекс текущей нагрузки сотрудника в баллах.
        /// Нагрузка взвешенная: каждый активный тикет даёт баллы по приоритету
        /// (Critical=4, High=3, Normal=2, Low=1). Вес тикета берётся из
        /// WorkloadService.GetTicketWeight — формула нагрузки едина для всего проекта.
        /// </summary>
        public async Task<int> CalculateEmployeeWorkloadAsync(
            int employeeId,
            CancellationToken ct = default)
        {
            var activeTickets = await _context.Tickets
                .AsNoTracking()
                .Where(t => t.AssigneeEmployeeId == employeeId
                         && t.Status != Constants.TicketStatus.Closed
                         && t.Status != Constants.TicketStatus.Resolved)
                .Select(t => t.Priority)
                .ToListAsync(ct);

            int load = activeTickets.Sum(p => WorkloadService.GetTicketWeight(p));

            // Ограничиваем шкалой индикатора, чтобы ProgressBar не "переполнялся".
            return Math.Min(load, MaxWorkloadPoints);
        }

        /// <summary>
        /// Рассчитывает SLA-compliance сотрудника за период, %.
        /// SLA-compliance = доля закрытых тикетов, у которых ClosedAt не позже DueAt.
        /// Если у сотрудника нет закрытых тикетов с заданным сроком — возвращает 100%
        /// (нарушений не было).
        /// </summary>
        public async Task<double> CalculateSlaComplianceAsync(
            int employeeId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            var resolved = await _context.Tickets
                .AsNoTracking()
                .Where(t => t.AssigneeEmployeeId == employeeId
                         && ClosedLikeStatuses.Contains(t.Status)
                         && t.ClosedAt != null
                         && t.DueAt != null
                         && t.ClosedAt >= fromUtc && t.ClosedAt <= toUtc)
                .Select(t => new { t.ClosedAt, t.DueAt })
                .ToListAsync(ct);

            if (resolved.Count == 0)
                return 100.0;

            int met = resolved.Count(t => t.ClosedAt!.Value <= t.DueAt!.Value);
            return Math.Round((double)met / resolved.Count * 100.0, 1);
        }

        /// <summary>
        /// Рассчитывает ART (Average Resolution Time) — среднее время разрешения
        /// тикетов сотрудника за период, в часах.
        /// Возвращает 0, если за период не было закрытых тикетов.
        /// </summary>
        public async Task<double> CalculateArtAsync(
            int employeeId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            var resolved = await _context.Tickets
                .AsNoTracking()
                .Where(t => t.AssigneeEmployeeId == employeeId
                         && ClosedLikeStatuses.Contains(t.Status)
                         && t.ClosedAt != null
                         && t.ClosedAt >= fromUtc && t.ClosedAt <= toUtc)
                .Select(t => new { t.ClosedAt, t.CreatedAt })
                .ToListAsync(ct);

            if (resolved.Count == 0)
                return 0.0;

            var hours = resolved
                .Select(t => (t.ClosedAt!.Value - t.CreatedAt).TotalHours)
                .Where(h => h >= 0)
                .ToList();

            return hours.Count > 0
                ? Math.Round(hours.Average(), 1)
                : 0.0;
        }

        // ==================================================================
        //  СВОДНЫЕ МЕТОДЫ (используются в окне AnalyticsWindow)
        // ==================================================================

        /// <summary>
        /// Рассчитывает сводные KPI по всей системе за указанный период.
        /// Период задаётся по дате создания тикета.
        /// </summary>
        public async Task<KpiSummary> GetSummaryAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            var tickets = await _context.Tickets
                .AsNoTracking()
                .Include(t => t.History)
                .Include(t => t.Comments)
                .Where(t => t.CreatedAt >= fromUtc && t.CreatedAt <= toUtc)
                .ToListAsync(ct);

            return BuildSummary(tickets);
        }

        /// <summary>
        /// Рассчитывает KPI по каждому сотруднику поддержки за период.
        /// Результат отсортирован по уровню SLA-compliance (от лучшего к худшему).
        /// </summary>
        public async Task<List<EmployeeKpi>> GetEmployeeKpisAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            var employees = await _context.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e => e.User.Role == Constants.UserRoles.Support
                         || e.User.Role == Constants.UserRoles.Admin)
                .ToListAsync(ct);

            var tickets = await _context.Tickets
                .AsNoTracking()
                .Include(t => t.History)
                .Include(t => t.Comments)
                .Include(t => t.Feedback)
                .Where(t => t.AssigneeEmployeeId != null
                         && t.CreatedAt >= fromUtc && t.CreatedAt <= toUtc)
                .ToListAsync(ct);

            var result = new List<EmployeeKpi>();

            foreach (var emp in employees)
            {
                var empTickets = tickets
                    .Where(t => t.AssigneeEmployeeId == emp.Id)
                    .ToList();

                var summary = BuildSummary(empTickets);

                var ratings = empTickets
                    .Where(t => t.Feedback != null)
                    .Select(t => t.Feedback!.Rating)
                    .ToList();

                result.Add(new EmployeeKpi
                {
                    EmployeeId = emp.Id,
                    Name = emp.Name,
                    Username = emp.User?.Username ?? "—",
                    TotalTickets = summary.TotalTickets,
                    ResolvedTickets = summary.ResolvedTickets,
                    SlaCompliancePercent = summary.SlaCompliancePercent,
                    AvgFirstResponseHours = summary.AvgFirstResponseHours,
                    AvgResolutionHours = summary.AvgResolutionHours,
                    OverdueRate = summary.OverdueRate,
                    ReopenRate = summary.ReopenRate,
                    FeedbackCount = ratings.Count,
                    AvgRating = ratings.Count > 0 ? ratings.Average() : 0.0
                });
            }

            return result
                .OrderByDescending(k => k.SlaCompliancePercent)
                .ThenByDescending(k => k.ResolvedTickets)
                .ToList();
        }

        // ------------------------------------------------------------------
        //  Внутренняя логика расчёта метрик
        // ------------------------------------------------------------------

        /// <summary>
        /// Формирует сводку KPI по переданному набору тикетов.
        /// Метод не обращается к БД — работает с уже загруженными данными,
        /// поэтому переиспользуется и для системы, и для отдельного сотрудника.
        /// </summary>
        private static KpiSummary BuildSummary(List<Ticket> tickets)
        {
            int total = tickets.Count;

            var resolved = tickets
                .Where(t => ClosedLikeStatuses.Contains(t.Status))
                .ToList();

            int resolvedCount = resolved.Count;

            // --- SLA Compliance ---
            var slaTracked = resolved
                .Where(t => t.DueAt.HasValue && t.ClosedAt.HasValue)
                .ToList();

            int slaMet = slaTracked.Count(t => t.ClosedAt!.Value <= t.DueAt!.Value);

            double slaPercent = slaTracked.Count > 0
                ? (double)slaMet / slaTracked.Count * 100.0
                : 0.0;

            // --- Время разрешения (часы) ---
            var resolutionHours = resolved
                .Where(t => t.ClosedAt.HasValue)
                .Select(t => (t.ClosedAt!.Value - t.CreatedAt).TotalHours)
                .Where(h => h >= 0)
                .ToList();

            double avgResolution = resolutionHours.Count > 0
                ? resolutionHours.Average()
                : 0.0;

            // --- Время первого ответа (часы) ---
            var frtHours = tickets
                .Select(GetFirstResponseHours)
                .Where(h => h.HasValue)
                .Select(h => h!.Value)
                .ToList();

            double avgFrt = frtHours.Count > 0
                ? frtHours.Average()
                : 0.0;

            // --- Доля просроченных ---
            int overdue = tickets.Count(t => t.IsOverdue);
            double overdueRate = total > 0
                ? (double)overdue / total * 100.0
                : 0.0;

            // --- Доля переоткрытых ---
            int reopened = tickets.Count(WasReopened);
            double reopenRate = total > 0
                ? (double)reopened / total * 100.0
                : 0.0;

            return new KpiSummary
            {
                TotalTickets = total,
                ResolvedTickets = resolvedCount,
                OpenTickets = tickets.Count(t => t.Status == Constants.TicketStatus.Open),
                InProgressTickets = tickets.Count(t => t.Status == Constants.TicketStatus.InProgress),
                OverdueTickets = overdue,
                SlaCompliancePercent = Math.Round(slaPercent, 1),
                AvgResolutionHours = Math.Round(avgResolution, 1),
                AvgFirstResponseHours = Math.Round(avgFrt, 1),
                OverdueRate = Math.Round(overdueRate, 1),
                ReopenRate = Math.Round(reopenRate, 1)
            };
        }

        /// <summary>
        /// Время первого ответа = разница между созданием тикета и первым
        /// внешним комментарием (виден клиенту). Внутренние комментарии
        /// сотрудников между собой не считаются ответом клиенту.
        /// Возвращает null, если ответа ещё не было.
        /// </summary>
        private static double? GetFirstResponseHours(Ticket ticket)
        {
            if (ticket.Comments == null || ticket.Comments.Count == 0)
                return null;

            var firstExternal = ticket.Comments
                .Where(c => !c.IsInternal && c.CreatedAt > ticket.CreatedAt)
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefault();

            if (firstExternal == null)
                return null;

            double hours = (firstExternal.CreatedAt - ticket.CreatedAt).TotalHours;
            return hours >= 0 ? hours : null;
        }

        /// <summary>
        /// Тикет считается переоткрытым, если в его истории есть переход
        /// статуса ИЗ Closed/Resolved обратно в активное состояние.
        /// Анализируем записи истории с действием "Статус".
        /// </summary>
        private static bool WasReopened(Ticket ticket)
        {
            if (ticket.History == null)
                return false;

            foreach (var h in ticket.History.Where(h => h.Action == "Статус"))
            {
                var parts = h.Details.Split("->", StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    continue;

                bool fromClosed = ClosedLikeStatuses.Contains(parts[0]);
                bool toActive = parts[1] == Constants.TicketStatus.Open
                             || parts[1] == Constants.TicketStatus.InProgress;

                if (fromClosed && toActive)
                    return true;
            }

            return false;
        }

        // ====== Публичные чистые методы для unit-тестирования ======

        /// <summary>
        /// Рассчитывает процент SLA-compliance: доля выполненных в срок заявок.
        /// </summary>
        public static double CalculateSlaPercent(int metInTime, int totalResolved)
        {
            if (totalResolved <= 0) return 0.0;
            return Math.Round((double)metInTime / totalResolved * 100.0, 1);
        }

        /// <summary>
        /// Рассчитывает долю просроченных заявок в процентах.
        /// </summary>
        public static double CalculateOverdueRate(int overdueCount, int totalCount)
        {
            if (totalCount <= 0) return 0.0;
            return Math.Round((double)overdueCount / totalCount * 100.0, 1);
        }

        /// <summary>
        /// Рассчитывает долю повторно открытых заявок (Reopen Rate) в процентах.
        /// </summary>
        public static double CalculateReopenRate(int reopenedCount, int totalCount)
        {
            if (totalCount <= 0) return 0.0;
            return Math.Round((double)reopenedCount / totalCount * 100.0, 1);
        }

        /// <summary>
        /// Возвращает среднее значение по списку, защищённое от пустого ввода.
        /// </summary>
        public static double SafeAverage(IEnumerable<double> values)
        {
            if (values == null) return 0.0;
            var list = values.ToList();
            return list.Count > 0 ? Math.Round(list.Average(), 1) : 0.0;
        }
    }

    /// <summary>
    /// Сводные KPI по набору тикетов (вся система или один сотрудник).
    /// </summary>
    public class KpiSummary
    {
        public int TotalTickets { get; set; }
        public int ResolvedTickets { get; set; }
        public int OpenTickets { get; set; }
        public int InProgressTickets { get; set; }
        public int OverdueTickets { get; set; }

        /// <summary>Доля тикетов, закрытых в срок, %.</summary>
        public double SlaCompliancePercent { get; set; }

        /// <summary>Среднее время разрешения тикета, часы.</summary>
        public double AvgResolutionHours { get; set; }

        /// <summary>Среднее время первого ответа клиенту, часы.</summary>
        public double AvgFirstResponseHours { get; set; }

        /// <summary>Доля просроченных тикетов, %.</summary>
        public double OverdueRate { get; set; }

        /// <summary>Доля переоткрытых тикетов, %.</summary>
        public double ReopenRate { get; set; }
    }

    /// <summary>
    /// KPI отдельного сотрудника поддержки за период.
    /// Используется для рейтинга и таблицы в окне аналитики.
    /// </summary>
    public class EmployeeKpi
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        public int TotalTickets { get; set; }
        public int ResolvedTickets { get; set; }

        public double SlaCompliancePercent { get; set; }
        public double AvgFirstResponseHours { get; set; }
        public double AvgResolutionHours { get; set; }
        public double OverdueRate { get; set; }
        public double ReopenRate { get; set; }

        public int FeedbackCount { get; set; }
        public double AvgRating { get; set; }
    }
}