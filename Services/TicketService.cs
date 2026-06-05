using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Services
{
    public class TicketService
    {
        private readonly AppDbContext _context;

        public TicketService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        
                                private async Task<int?> GetBestEmployeeForTicketAsync(TicketPriority newTicketPriority, CancellationToken ct)
        {
            var candidates = await _context.Employees
                .Include(e => e.User)
                .Where(e => e.User.Role == Constants.UserRoles.Support)
                .ToListAsync(ct);

            if (!candidates.Any()) return null;

                        var activeTickets = await _context.Tickets
                .Where(t => t.Status != Constants.TicketStatus.Closed && t.Status != Constants.TicketStatus.Resolved && t.AssigneeEmployeeId != null)
                .Select(t => new { t.AssigneeEmployeeId, t.Priority })
                .ToListAsync(ct);

                        var workloadDict = candidates.ToDictionary(c => c.Id, c => 0);

                        foreach (var t in activeTickets)
            {
                if (t.AssigneeEmployeeId.HasValue && workloadDict.ContainsKey(t.AssigneeEmployeeId.Value))
                {
                    workloadDict[t.AssigneeEmployeeId.Value] += WorkloadService.GetTicketWeight(t.Priority);
                }
            }

                        var bestCandidate = workloadDict.OrderBy(kvp => kvp.Value).First();
            int newTicketWeight = WorkloadService.GetTicketWeight(newTicketPriority);

                        if (bestCandidate.Value + newTicketWeight > KpiService.MaxWorkloadPoints)
            {
                return null;
            }

            return bestCandidate.Key;
        }

                                private DateTime CalculateDeadline(TicketPriority priority)
        {
            var now = DateTime.UtcNow;
            return priority switch
            {
                TicketPriority.Critical => now.AddHours(4),                   TicketPriority.High => now.AddHours(24),                      TicketPriority.Normal => now.AddDays(3),                      TicketPriority.Low => now.AddDays(7),                         _ => now.AddDays(3)
            };
        }

        
        public async Task<Ticket> CreateAsync(
            int clientId,
            string title,
            string description,
            DateTime? manualDueAt = null,
            int? authorUserId = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title is required", nameof(title));

                        var classifier = new TicketClassifier(new RuleBasedTicketClassifier());
            var (category, priority, _) = classifier.Classify(title, description ?? string.Empty);

            string classificationNote = "Автоматическое определение";

                        var dueAt = manualDueAt ?? CalculateDeadline(priority);

                        int? assigneeId = await GetBestEmployeeForTicketAsync(priority, ct);
            string status = assigneeId.HasValue ? Constants.TicketStatus.InProgress : Constants.TicketStatus.Open;

            var ticket = new Ticket
            {
                ClientId = clientId,
                Title = title.Trim(),
                Description = description ?? string.Empty,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                DueAt = dueAt,
                UserId = authorUserId,
                Category = category,
                Priority = priority,
                AssigneeEmployeeId = assigneeId,
                History = new List<TicketHistory>()
            };

                        ticket.History.Add(new TicketHistory
            {
                Action = "Классификация обращения",
                Details = $"Способ: {classificationNote}\nКатегория: {category} | Приоритет: {priority}\nСрок (SLA): {dueAt:dd.MM.yy HH:mm}",
                Timestamp = DateTime.UtcNow
            });

            if (assigneeId.HasValue)
            {
                var empName = await _context.Employees.Where(e => e.Id == assigneeId).Select(e => e.Name).FirstOrDefaultAsync(ct);
                int weight = WorkloadService.GetTicketWeight(priority);

                ticket.History.Add(new TicketHistory
                {
                    Action = "Авто-маршрутизация",
                    Details = $"Назначен оператор: {empName} (Влияние на нагрузку: +{weight} баллов)",
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                ticket.History.Add(new TicketHistory
                {
                    Action = "Очередь",
                    Details = "Система не смогла назначить тикет: все инженеры превысили лимит нагрузки. Тикет помещен в очередь (Open).",
                    Timestamp = DateTime.UtcNow
                });
            }

            await _context.Tickets.AddAsync(ticket, ct);
            await _context.SaveChangesAsync(ct);

            return ticket;
        }

        public async Task<bool> AssignAsync(int ticketId, int employeeId, CancellationToken ct = default)
        {
            var t = await _context.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return false;

            var emp = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (emp == null) throw new InvalidOperationException("Сотрудник не найден");

                        var kpiService = new KpiService(_context);
            int currentLoad = await kpiService.CalculateEmployeeWorkloadAsync(employeeId);
            int ticketWeight = WorkloadService.GetTicketWeight(t.Priority);

                        if (t.AssigneeEmployeeId != employeeId && (currentLoad + ticketWeight > KpiService.MaxWorkloadPoints))
            {
                throw new InvalidOperationException(
                    $"ВНИМАНИЕ: Назначение заблокировано!\n\n" +
                    $"Сотрудник перегружен. Текущая нагрузка: {currentLoad} / {KpiService.MaxWorkloadPoints} баллов.\n" +
                    $"Вес заявки: {ticketWeight} баллов (Приоритет: {t.Priority}).\n\n" +
                    $"Назначьте заявку на другого специалиста.");
            }
            
            var oldStatus = t.Status;
            t.AssigneeEmployeeId = employeeId;

            if (t.Status == Constants.TicketStatus.Open)
                t.Status = Constants.TicketStatus.InProgress;

            t.UpdatedAt = DateTime.UtcNow;

            await _context.TicketHistories.AddAsync(new TicketHistory
            {
                TicketId = t.Id,
                Action = "Назначение",
                Details = $"Назначен сотрудник: {emp.Name}. Текущая нагрузка сотрудника: {currentLoad + ticketWeight} баллов.",
                Timestamp = DateTime.UtcNow
            }, ct);

            if (oldStatus != t.Status)
            {
                await _context.TicketHistories.AddAsync(new TicketHistory
                {
                    TicketId = t.Id,
                    Action = "Статус",
                    Details = $"{oldStatus} -> {t.Status}",
                    Timestamp = DateTime.UtcNow
                }, ct);
            }

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<Ticket?> UpdateAsync(
            int ticketId,
            string? title = null,
            string? description = null,
            DateTime? dueAt = null,
            CancellationToken ct = default)
        {
            var t = await _context.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return null;

            var oldTitle = t.Title;
            var oldDesc = t.Description;
            var oldDue = t.DueAt;

            if (!string.IsNullOrWhiteSpace(title)) t.Title = title.Trim();
            if (description != null) t.Description = description;

            if (dueAt.HasValue) t.DueAt = dueAt;

            t.UpdatedAt = DateTime.UtcNow;

            await _context.TicketHistories.AddAsync(new TicketHistory
            {
                TicketId = t.Id,
                Action = "Обновление",
                Details = BuildUpdateDetails(oldTitle, t.Title, oldDesc, t.Description, oldDue, t.DueAt),
                Timestamp = DateTime.UtcNow
            }, ct);

            await _context.SaveChangesAsync(ct);
            return t;
        }

        public async Task<bool> DeleteAsync(int ticketId, CancellationToken ct = default)
        {
            var t = await _context.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return false;

            _context.Tickets.Remove(t);
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ChangeStatusAsync(int ticketId, string newStatus, CancellationToken ct = default)
        {
            if (!Ticket.AllStatuses.Contains(newStatus))
                throw new ArgumentException($"Недопустимый статус: {newStatus}", nameof(newStatus));

            var t = await _context.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return false;

            var oldStatus = t.Status;
            if (oldStatus == newStatus) return true;

            t.Status = newStatus;
            t.UpdatedAt = DateTime.UtcNow;

            await _context.TicketHistories.AddAsync(new TicketHistory
            {
                TicketId = t.Id,
                Action = "Статус",
                Details = $"{oldStatus} -> {newStatus}",
                Timestamp = DateTime.UtcNow
            }, ct);

            if (newStatus == Constants.TicketStatus.Closed)
                t.ClosedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> SetDueDateAsync(int ticketId, DateTime? dueAt, CancellationToken ct = default)
        {
            var t = await _context.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return false;

            var old = t.DueAt;
            t.DueAt = dueAt;
            t.UpdatedAt = DateTime.UtcNow;

            await _context.TicketHistories.AddAsync(new TicketHistory
            {
                TicketId = t.Id,
                Action = "Дедлайн",
                Details = BuildDueDetails(old, dueAt),
                Timestamp = DateTime.UtcNow
            }, ct);

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<TicketComment> AddCommentAsync(
            int ticketId,
            int userId,
            string text,
            bool isInternal = true,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Комментарий не может быть пустым", nameof(text));

            var comment = new TicketComment
            {
                TicketId = ticketId,
                UserId = userId,
                Text = text.Trim(),
                IsInternal = isInternal,
                CreatedAt = DateTime.UtcNow
            };

            await _context.TicketComments.AddAsync(comment, ct);

            await _context.TicketHistories.AddAsync(new TicketHistory
            {
                TicketId = ticketId,
                Action = isInternal ? "[Internal] Комментарий" : "Комментарий",
                Details = text.Trim(),
                Timestamp = DateTime.UtcNow
            }, ct);

            await _context.SaveChangesAsync(ct);
            return comment;
        }

        public async Task<bool> CloseAsync(
            int ticketId,
            string resolutionText,
            int resolvedByEmployeeId,
            CancellationToken ct = default)
        {
            var t = await _context.Tickets.Include(x => x.Solution).FirstOrDefaultAsync(x => x.Id == ticketId, ct);
            if (t == null) return false;

            await ChangeStatusAsync(ticketId, Constants.TicketStatus.Closed, ct);

            if (t.Solution == null)
            {
                t.Solution = new Solution
                {
                    TicketId = t.Id,
                    ResolutionText = resolutionText.Trim(),
                    ResolutionDate = DateTime.UtcNow
                };
                await _context.Solutions.AddAsync(t.Solution, ct);
            }
            else
            {
                t.Solution.ResolutionText = resolutionText.Trim();
                t.Solution.ResolutionDate = DateTime.UtcNow;
            }

            t.UpdatedAt = DateTime.UtcNow;
            t.ClosedAt = DateTime.UtcNow;

            await _context.TicketHistories.AddAsync(new TicketHistory
            {
                TicketId = t.Id,
                Action = "Закрытие",
                Details = $"Тикет закрыт сотрудником #{resolvedByEmployeeId}. Решение зафиксировано.",
                Timestamp = DateTime.UtcNow
            }, ct);

            await _context.SaveChangesAsync(ct);
            return true;
        }

        private static string BuildUpdateDetails(string oldTitle, string newTitle, string oldDesc, string newDesc, DateTime? oldDue, DateTime? newDue)
        {
            var parts = new List<string>();
            if (!string.Equals(oldTitle, newTitle, StringComparison.Ordinal)) parts.Add($"Заголовок изменен");
            if (!string.Equals(oldDesc, newDesc, StringComparison.Ordinal)) parts.Add("Описание изменено");
            if (oldDue != newDue) parts.Add($"Срок: {FormatDate(oldDue)} -> {FormatDate(newDue)}");
            return parts.Count == 0 ? "Изменений нет" : string.Join("; ", parts);
        }

        private static string BuildDueDetails(DateTime? oldDue, DateTime? newDue) => $"Срок: {FormatDate(oldDue)} -> {FormatDate(newDue)}";
        private static string FormatDate(DateTime? dt) => dt.HasValue ? dt.Value.ToString("dd.MM.yyyy HH:mm") : "не задан";
    }
}