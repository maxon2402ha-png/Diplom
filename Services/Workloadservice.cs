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
    /// Уровень загруженности оператора относительно его лимита.
    /// </summary>
    public enum WorkloadLevel
    {
        /// <summary>Загрузка в норме (до 70% лимита).</summary>
        Normal = 0,

        /// <summary>Высокая загрузка (70–100% лимита).</summary>
        High = 1,

        /// <summary>Перегрузка (свыше 100% лимита).</summary>
        Overloaded = 2
    }

    /// <summary>
    /// Сервис анализа нагрузки сотрудников технической поддержки.
    ///
    /// В отличие от примитивного подсчёта "сколько открытых тикетов",
    /// сервис использует ВЗВЕШЕННУЮ нагрузку: каждый активный тикет
    /// даёт вес в зависимости от приоритета. Это отражает тот факт,
    /// что критичный тикет требует больше ресурсов, чем низкоприоритетный.
    ///
    /// Взвешенная нагрузка сравнивается с лимитом сотрудника
    /// (Employee.MaxActiveTickets) для выявления перегрузки.
    /// </summary>
    public class WorkloadService
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Веса приоритетов. Критичный тикет "весит" вчетверо больше низкого.
        /// Значения вынесены в одно место — их легко обосновать в работе
        /// и при необходимости откалибровать.
        /// </summary>
        private static readonly Dictionary<TicketPriority, int> PriorityWeights = new()
        {
            { TicketPriority.Low,      1 },
            { TicketPriority.Normal,   2 },
            { TicketPriority.High,     3 },
            { TicketPriority.Critical, 4 }
        };

        // Порог, выше которого загрузка считается "высокой" (доля от лимита).
        private const double HighLoadThreshold = 0.70;

        public WorkloadService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Возвращает вес одного тикета по его приоритету.
        /// </summary>
        public static int GetTicketWeight(TicketPriority priority)
            => PriorityWeights.TryGetValue(priority, out var w) ? w : 2;

        /// <summary>
        /// Рассчитывает текущую нагрузку всех операторов поддержки.
        /// Результат отсортирован по убыванию загрузки —
        /// перегруженные сотрудники оказываются сверху.
        /// </summary>
        public async Task<List<EmployeeWorkload>> GetWorkloadAsync(CancellationToken ct = default)
        {
            // Операторы поддержки (Admin тоже может вести тикеты).
            var employees = await _context.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e => e.User.Role == Constants.UserRoles.Support
                         || e.User.Role == Constants.UserRoles.Admin)
                .ToListAsync(ct);

            // Все активные (незакрытые) тикеты с назначенным исполнителем.
            var activeTickets = await _context.Tickets
                .AsNoTracking()
                .Where(t => t.AssigneeEmployeeId != null
                         && t.Status != Constants.TicketStatus.Closed
                         && t.Status != Constants.TicketStatus.Resolved)
                .Select(t => new { t.AssigneeEmployeeId, t.Priority, t.Status })
                .ToListAsync(ct);

            var result = new List<EmployeeWorkload>();

            foreach (var emp in employees)
            {
                var empTickets = activeTickets
                    .Where(t => t.AssigneeEmployeeId == emp.Id)
                    .ToList();

                int activeCount = empTickets.Count;

                // Взвешенная нагрузка — сумма весов всех активных тикетов.
                int weightedLoad = empTickets.Sum(t => GetTicketWeight(t.Priority));

                // Лимит сотрудника. Если не задан (0) — берём значение
                // по умолчанию, чтобы избежать деления на ноль.
                int limit = emp.MaxActiveTickets > 0 ? emp.MaxActiveTickets : 5;

                // Лимит задан в "тикетах", а нагрузка — взвешенная.
                // Приводим лимит к той же шкале, умножая на средний вес
                // обычного тикета (Normal = 2), чтобы сравнение было честным.
                int weightedLimit = limit * GetTicketWeight(TicketPriority.Normal);

                double loadRatio = weightedLimit > 0
                    ? (double)weightedLoad / weightedLimit
                    : 0.0;

                result.Add(new EmployeeWorkload
                {
                    EmployeeId = emp.Id,
                    Name = emp.Name,
                    Username = emp.User?.Username ?? "—",
                    ActiveTickets = activeCount,
                    WeightedLoad = weightedLoad,
                    MaxActiveTickets = limit,
                    LoadPercent = Math.Round(loadRatio * 100.0, 1),
                    Level = ClassifyLevel(loadRatio),
                    CriticalCount = empTickets.Count(t => t.Priority == TicketPriority.Critical),
                    HighCount = empTickets.Count(t => t.Priority == TicketPriority.High)
                });
            }

            return result
                .OrderByDescending(w => w.LoadPercent)
                .ToList();
        }

        /// <summary>
        /// Выбирает наименее загруженного оператора для назначения нового тикета.
        /// Учитывает взвешенную нагрузку, а не просто число тикетов.
        /// Перегруженные операторы пропускаются, если есть свободные.
        ///
        /// Это улучшенная замена метода GetBestEmployeeForTicketAsync
        /// из TicketService.
        /// </summary>
        public async Task<int?> GetLeastLoadedEmployeeAsync(CancellationToken ct = default)
        {
            var workloads = await GetWorkloadAsync(ct);

            if (workloads.Count == 0)
                return null;

            // Сначала ищем среди не перегруженных операторов.
            var available = workloads
                .Where(w => w.Level != WorkloadLevel.Overloaded)
                .ToList();

            // Если все перегружены — выбираем наименее загруженного из всех.
            var pool = available.Count > 0 ? available : workloads;

            // Операторов в системе может не быть вовсе — тогда назначать некого.
            if (pool.Count == 0)
                return null;

            return pool
                .OrderBy(w => w.WeightedLoad)
                .ThenBy(w => w.ActiveTickets)
                .First()
                .EmployeeId;
        }

        /// <summary>
        /// Возвращает сводку по нагрузке отдела: сколько операторов
        /// перегружено, средняя загрузка и т.п.
        /// </summary>
        public async Task<WorkloadSummary> GetSummaryAsync(CancellationToken ct = default)
        {
            var workloads = await GetWorkloadAsync(ct);

            return new WorkloadSummary
            {
                TotalEmployees = workloads.Count,
                OverloadedCount = workloads.Count(w => w.Level == WorkloadLevel.Overloaded),
                HighLoadCount = workloads.Count(w => w.Level == WorkloadLevel.High),
                NormalCount = workloads.Count(w => w.Level == WorkloadLevel.Normal),
                AvgLoadPercent = workloads.Count > 0
                    ? Math.Round(workloads.Average(w => w.LoadPercent), 1)
                    : 0.0,
                TotalActiveTickets = workloads.Sum(w => w.ActiveTickets)
            };
        }

        /// <summary>
        /// Классифицирует уровень загрузки по доле от лимита.
        /// Выделен в публичный метод для unit-тестирования.
        /// </summary>
        public static WorkloadLevel ClassifyLevel(double loadRatio)
        {
            if (loadRatio > 1.0)
                return WorkloadLevel.Overloaded;

            if (loadRatio >= HighLoadThreshold)
                return WorkloadLevel.High;

            return WorkloadLevel.Normal;
        }
    }

    /// <summary>
    /// Нагрузка одного оператора поддержки.
    /// </summary>
    public class EmployeeWorkload
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        /// <summary>Количество активных (незакрытых) тикетов.</summary>
        public int ActiveTickets { get; set; }

        /// <summary>Взвешенная нагрузка — сумма весов активных тикетов.</summary>
        public int WeightedLoad { get; set; }

        /// <summary>Лимит активных тикетов из карточки сотрудника.</summary>
        public int MaxActiveTickets { get; set; }

        /// <summary>Загрузка в процентах от лимита.</summary>
        public double LoadPercent { get; set; }

        /// <summary>Уровень загрузки: норма / высокая / перегрузка.</summary>
        public WorkloadLevel Level { get; set; }

        /// <summary>Количество критичных тикетов в работе.</summary>
        public int CriticalCount { get; set; }

        /// <summary>Количество высокоприоритетных тикетов в работе.</summary>
        public int HighCount { get; set; }

        /// <summary>Текстовое описание уровня — удобно для биндинга в WPF.</summary>
        public string LevelText => Level switch
        {
            WorkloadLevel.Overloaded => "Перегружен",
            WorkloadLevel.High => "Высокая загрузка",
            _ => "Норма"
        };
    }

    /// <summary>
    /// Сводка по нагрузке всего отдела поддержки.
    /// </summary>
    public class WorkloadSummary
    {
        public int TotalEmployees { get; set; }
        public int OverloadedCount { get; set; }
        public int HighLoadCount { get; set; }
        public int NormalCount { get; set; }
        public double AvgLoadPercent { get; set; }
        public int TotalActiveTickets { get; set; }
    }
}