using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class DashboardControl : UserControl, INotifyPropertyChanged
    {
        private readonly AuthService _auth;
        private readonly AuditService _audit;
        private readonly DispatcherTimer _timer;

        private List<Ticket> _periodTickets = [];
        private List<Ticket> _allActiveTickets = [];

        private UserUiSettings? _userSettings;
        private int _defaultPeriodDays = 30;

        private string? _currentUserName;
        public string? CurrentUserName
        {
            get => _currentUserName;
            set { _currentUserName = value; OnPropertyChanged(); }
        }

        private string? _currentUserRole;
        public string? CurrentUserRole
        {
            get => _currentUserRole;
            set { _currentUserRole = value; OnPropertyChanged(); }
        }

        private string _lastUpdatedText = "Ожидание...";
        public string LastUpdatedText
        {
            get => _lastUpdatedText;
            set { _lastUpdatedText = value; OnPropertyChanged(); }
        }

        private int _kpiTotal;
        public int KpiTotal { get => _kpiTotal; set { _kpiTotal = value; OnPropertyChanged(); } }

        private int _kpiOpen;
        public int KpiOpen { get => _kpiOpen; set { _kpiOpen = value; OnPropertyChanged(); } }

        private int _kpiInProgress;
        public int KpiInProgress { get => _kpiInProgress; set { _kpiInProgress = value; OnPropertyChanged(); } }

        private int _kpiClosed;
        public int KpiClosed { get => _kpiClosed; set { _kpiClosed = value; OnPropertyChanged(); } }

        private int _kpiOverdue;
        public int KpiOverdue { get => _kpiOverdue; set { _kpiOverdue = value; OnPropertyChanged(); } }

        private string _kpiResolvedPercentText = "0%";
        public string KpiResolvedPercentText { get => _kpiResolvedPercentText; set { _kpiResolvedPercentText = value; OnPropertyChanged(); } }

        private string _kpiAvgResolutionText = "—";
        public string KpiAvgResolutionText { get => _kpiAvgResolutionText; set { _kpiAvgResolutionText = value; OnPropertyChanged(); } }

        public SeriesCollection SeriesTicketsByDay { get; } = [];
        public SeriesCollection SeriesCategories { get; } = [];
        public SeriesCollection SeriesPriorities { get; } = [];
        public SeriesCollection SeriesByAssignee { get; } = [];

        private string[] _labelsDays = [];
        public string[] LabelsDays
        {
            get => _labelsDays;
            set { _labelsDays = value; OnPropertyChanged(); }
        }

        private string[] _labelsAssignees = [];
        public string[] LabelsAssignees
        {
            get => _labelsAssignees;
            set { _labelsAssignees = value; OnPropertyChanged(); }
        }

        public Func<double, string> CountFormatter { get; set; } = v => v.ToString("N0");

        public DashboardControl(AuthService authService)
            : this(authService, new AuditService(authService))
        {
        }

        public DashboardControl(AuthService authService, AuditService auditService)
        {
            InitializeComponent();
            DataContext = this;

            _auth = authService ?? throw new ArgumentNullException(nameof(authService));
            _audit = auditService ?? throw new ArgumentNullException(nameof(auditService));

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += async (s, e) => await LoadFromDbAsync();

            if (CheckUserAccess())
            {
                var user = _auth.CurrentUser;
                if (user != null)
                {
                    CurrentUserName = user.Username;
                    CurrentUserRole = user.Role;
                }

                ApplyUserSettings();
                InitializeFilters();

                Loaded += async (s, e) =>
                {
                    await LoadFromDbAsync();
                    if (_userSettings?.RefreshRateSeconds > 0)
                        _timer.Start();
                };
                Unloaded += (s, e) => _timer.Stop();
            }
            else
            {
                Visibility = Visibility.Collapsed;
                MessageBox.Show("Нет доступа к Дашборду.", "Отказано в доступе", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool CheckUserAccess()
        {
            var user = _auth.CurrentUser;
            if (user == null) return false;

            return user.Role == Constants.UserRoles.Admin ||
                   user.Role == Constants.UserRoles.Support;
        }

        private void ApplyUserSettings()
        {
            try
            {
                var userId = _auth.CurrentUser?.Id ?? 0;
                using var context = App.CreateDbContext();

                _userSettings = context.UserUiSettings
                    .FirstOrDefault(s => s.UserId == userId);

                if (_userSettings == null)
                {
                    _userSettings = new UserUiSettings { UserId = userId };
                    context.UserUiSettings.Add(_userSettings);
                    context.SaveChanges();
                }

                if (FindName("KpiSection") is FrameworkElement kpiSection)
                    kpiSection.Visibility = _userSettings.ShowKpiBlock ? Visibility.Visible : Visibility.Collapsed;

                if (FindName("ChartsSection") is FrameworkElement chartsSection)
                    chartsSection.Visibility = _userSettings.ShowChartsBlock ? Visibility.Visible : Visibility.Collapsed;

                if (FindName("TableSection") is FrameworkElement tableSection)
                    tableSection.Visibility = _userSettings.ShowDetailedTable ? Visibility.Visible : Visibility.Collapsed;

                if (_userSettings.RefreshRateSeconds > 0)
                {
                    _timer.Interval = TimeSpan.FromSeconds(_userSettings.RefreshRateSeconds);
                    if (IsLoaded) _timer.Start();
                }
                else
                {
                    _timer.Stop();
                }

                _defaultPeriodDays = _userSettings.DefaultPeriodDays > 0
                    ? _userSettings.DefaultPeriodDays
                    : 30;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying settings: {ex.Message}");
                _defaultPeriodDays = 30;
            }
        }

        private void InitializeFilters()
        {
            ToDatePicker.SelectedDate = DateTime.Now;
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-_defaultPeriodDays);

            PopulateAssignees();

            List<object> catList = ["Все", .. Enum.GetValues(typeof(TicketCategory)).Cast<object>()];
            CategoryFilter.ItemsSource = catList;

            List<object> prioList = ["Все", .. Enum.GetValues(typeof(TicketPriority)).Cast<object>()];
            PriorityFilter.ItemsSource = prioList;

            SafeSelectIndex(StatusFilter, 0);
            SafeSelectIndex(CategoryFilter, 0);
            SafeSelectIndex(PriorityFilter, 0);
            SafeSelectIndex(AssigneeFilter, 0);
            SafeSelectIndex(GroupByCombo, 0);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var userId = _auth.CurrentUser?.Id ?? 0;

            var wnd = new UserSettingsWindow(userId)
            {
                Owner = Window.GetWindow(this)
            };

            if (wnd.ShowDialog() == true)
            {
                ApplyUserSettings();
                _ = LoadFromDbAsync();
            }
        }

        private static void SafeSelectIndex(ComboBox box, int index)
        {
            if (box.Items.Count > index)
                box.SelectedIndex = index;
        }

        private void PopulateAssignees()
        {
            try
            {
                using var context = App.CreateDbContext();

                List<string> names = [.. context.Employees
                    .Include(e => e.User)
                    .AsNoTracking()
                    .Where(e => e.User != null)
                    .Select(e => e.User!.Username)
                    .Distinct()
                    .OrderBy(n => n)];

                names.Insert(0, "Все");

                AssigneeFilter.ItemsSource = names;
                AssigneeFilter.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private async Task LoadFromDbAsync()
        {
            try
            {
                LastUpdatedText = "Загрузка...";

                var endDate = ToDatePicker.SelectedDate?.Date.AddDays(1).AddSeconds(-1) ?? DateTime.UtcNow;
                var startDate = FromDatePicker.SelectedDate?.Date ?? endDate.AddDays(-365);
                var startUtc = startDate.ToUniversalTime();
                var endUtc = endDate.ToUniversalTime();

                using (var db = App.CreateDbContext())
                {
                    _periodTickets = await db.Tickets
                        .Include(t => t.Solution)
                        .Include(t => t.Assignee).ThenInclude(a => a.User)
                        .AsNoTracking()
                        .Where(t => t.CreatedAt >= startUtc && t.CreatedAt <= endUtc)
                        .ToListAsync();

                    var openTickets = await db.Tickets
                        .AsNoTracking()
                        .Where(t => t.Status != Constants.TicketStatus.Closed)
                        .ToListAsync();

                    _allActiveTickets = [.. openTickets.Union(_periodTickets.Where(t => t.Status == Constants.TicketStatus.Closed))];
                }

                UpdateDashboard();

                await UpdatePerformanceMatrixAsync();

                LastUpdatedText = $"Обновлено: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                LastUpdatedText = "Ошибка";
                Debug.WriteLine($"Ошибка загрузки дашборда: {ex.Message}");
            }
        }

        private async Task UpdatePerformanceMatrixAsync()
        {
            try
            {
                var fromDate = FromDatePicker.SelectedDate?.Date.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);
                var toDate = ToDatePicker.SelectedDate?.Date.AddDays(1).AddSeconds(-1).ToUniversalTime() ?? DateTime.UtcNow;

                using var db = App.CreateDbContext();
                var kpiService = new KpiService(db);
                var workloadService = new WorkloadService(db);

                var employeeKpis = await kpiService.GetEmployeeKpisAsync(fromDate, toDate);
                var workloads = await workloadService.GetWorkloadAsync();

                var workloadByEmployee = workloads.ToDictionary(w => w.EmployeeId);

                var matrixData = new List<EmployeePerformanceVm>();

                foreach (var kpi in employeeKpis)
                {
                    workloadByEmployee.TryGetValue(kpi.EmployeeId, out var wl);

                    matrixData.Add(new EmployeePerformanceVm
                    {
                        Name = kpi.Name,
                        ActiveTickets = wl?.ActiveTickets ?? 0,
                        WorkloadPoints = wl?.WeightedLoad ?? 0,
                        SlaPercentage = kpi.SlaCompliancePercent,
                        ArtHours = kpi.AvgResolutionHours
                    });
                }

                PerformanceMatrixGrid.ItemsSource = matrixData.OrderByDescending(m => m.WorkloadPoints).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка расчета матрицы компетенций: {ex.Message}");
            }
        }

        private void UpdateDashboard()
        {
            try
            {
                string statusFilter = GetComboText(StatusFilter);
                string assigneeFilter = (AssigneeFilter.SelectedItem as string) ?? GetComboText(AssigneeFilter);
                object? catSelected = CategoryFilter.SelectedItem;
                object? prioSelected = PriorityFilter.SelectedItem;

                var kpiSet = ApplyFilters(_allActiveTickets, statusFilter, assigneeFilter, catSelected, prioSelected);
                CalculateKpi(kpiSet);

                _periodTickets ??= [];

                var categoryChartSet = ApplyFilters(_periodTickets, statusFilter, assigneeFilter, null, prioSelected);
                var priorityChartSet = ApplyFilters(_periodTickets, statusFilter, assigneeFilter, catSelected, null);
                var assigneeChartSet = ApplyFilters(_periodTickets, statusFilter, "Все", catSelected, prioSelected);

                BuildCharts(_periodTickets, categoryChartSet, priorityChartSet, assigneeChartSet);

                // Оставляем ToList(), так как ItemsSource имеет тип IEnumerable
                DashboardTicketsGrid.ItemsSource = _periodTickets.OrderByDescending(t => t.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dashboard Update Error: {ex.Message}");
            }
        }

        private static List<Ticket> ApplyFilters(
            List<Ticket> source,
            string status,
            string assignee,
            object? category,
            object? priority)
        {
            var query = source.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(status) && status != "Все")
            {
                if (status == "In Progress")
                    query = query.Where(t => t.Status == Constants.TicketStatus.InProgress);
                else
                    query = query.Where(t => t.Status == status);
            }

            if (category is TicketCategory catVal)
                query = query.Where(t => t.Category == catVal);

            if (priority is TicketPriority prioVal)
                query = query.Where(t => t.Priority == prioVal);

            if (!string.IsNullOrWhiteSpace(assignee) && assignee != "Все")
                query = query.Where(t =>
                    t.Assignee != null &&
                    t.Assignee.User != null &&
                    t.Assignee.User.Username == assignee);

            return [.. query];
        }

        private void CalculateKpi(List<Ticket> tickets)
        {
            int total = tickets.Count;
            int closed = tickets.Count(t => t.Status == Constants.TicketStatus.Closed);
            int open = tickets.Count(t => t.Status == Constants.TicketStatus.Open);
            int inProgress = tickets.Count(t => t.Status == Constants.TicketStatus.InProgress);

            int overdue = tickets.Count(t => t.Status != Constants.TicketStatus.Closed && t.DueAt.HasValue && t.DueAt.Value < DateTime.UtcNow);

            KpiTotal = total;
            KpiOpen = open;
            KpiInProgress = inProgress;
            KpiClosed = closed;
            KpiOverdue = overdue;

            double percent = total == 0 ? 0 : (double)closed / total * 100.0;
            KpiResolvedPercentText = $"{percent:0.#}%";

            List<double> resolvedTimes = [.. tickets
                .Where(t => t.Status == Constants.TicketStatus.Closed && t.ClosedAt.HasValue)
                .Select(t => (t.ClosedAt!.Value - t.CreatedAt).TotalHours)];

            if (resolvedTimes.Count > 0)
            {
                double avgHours = resolvedTimes.Average();
                KpiAvgResolutionText = avgHours < 24
                    ? $"{avgHours:0.#} ч"
                    : $"{(avgHours / 24):0.#} дн";
            }
            else
            {
                KpiAvgResolutionText = "—";
            }
        }

        private void BuildCharts(
            List<Ticket> periodSet,
            List<Ticket> catSet,
            List<Ticket> prioSet,
            List<Ticket> assignSet)
        {
            var groupType = GetComboText(GroupByCombo);

            var grouped = periodSet
                .GroupBy(t =>
                    groupType == "Месяцы"
                        ? new DateTime(t.CreatedAt.Year, t.CreatedAt.Month, 1)
                        : t.CreatedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() });

            int sortDaysIndex = 0;
            Dispatcher.Invoke(() => { sortDaysIndex = SortDaysCombo.SelectedIndex; });

            if (sortDaysIndex == 1)
                grouped = grouped.OrderByDescending(x => x.Date);
            else
                grouped = grouped.OrderBy(x => x.Date);

            var groupedList = grouped.ToList();

            SeriesTicketsByDay.Clear();

            if (groupedList.Count > 0)
            {
                var format = groupType == "Месяцы" ? "MMM yy" : "dd.MM";
                LabelsDays = [.. groupedList.Select(x => x.Date.ToString(format))];

                SeriesTicketsByDay.Add(new ColumnSeries
                {
                    Title = "Тикеты",
                    Values = new ChartValues<int>(groupedList.Select(x => x.Count)),
                    DataLabels = true
                });
            }
            else
            {
                LabelsDays = ["-"];
                SeriesTicketsByDay.Add(new ColumnSeries
                {
                    Values = new ChartValues<int> { 0 },
                    DataLabels = false
                });
            }

            var catGroups = catSet
                .GroupBy(t => t.Category)
                .Select(g => new { Name = g.Key.ToString(), Count = g.Count() });

            int catSortIndex = 0;
            Dispatcher.Invoke(() => { catSortIndex = CategorySortCombo.SelectedIndex; });

            if (catSortIndex == 1)
                catGroups = catGroups.OrderBy(x => x.Name);
            else
                catGroups = catGroups.OrderByDescending(x => x.Count);

            var catList = catGroups.ToList();
            SeriesCategories.Clear();

            if (catList.Count == 0)
            {
                SeriesCategories.Add(new PieSeries
                {
                    Title = "Нет данных",
                    Values = new ChartValues<int> { 1 },
                    DataLabels = false,
                    Fill = Brushes.LightGray
                });
            }
            else
            {
                foreach (var g in catList)
                {
                    SeriesCategories.Add(new PieSeries
                    {
                        Title = g.Name,
                        Values = new ChartValues<int> { g.Count },
                        DataLabels = true
                    });
                }
            }

            var prioGroups = prioSet
                .GroupBy(t => t.Priority)
                .Select(g => new
                {
                    Name = g.Key.ToString(),
                    Count = g.Count(),
                    Val = (int)g.Key
                });

            int prioSortIndex = 0;
            Dispatcher.Invoke(() => { prioSortIndex = PrioritySortCombo.SelectedIndex; });

            if (prioSortIndex == 1)
                prioGroups = prioGroups.OrderByDescending(x => x.Val);
            else
                prioGroups = prioGroups.OrderByDescending(x => x.Count);

            var prioList = prioGroups.ToList();
            SeriesPriorities.Clear();

            if (prioList.Count == 0)
            {
                SeriesPriorities.Add(new PieSeries
                {
                    Title = "Нет данных",
                    Values = new ChartValues<int> { 1 },
                    DataLabels = false,
                    Fill = Brushes.LightGray
                });
            }
            else
            {
                foreach (var g in prioList)
                {
                    SeriesPriorities.Add(new PieSeries
                    {
                        Title = g.Name,
                        Values = new ChartValues<int> { g.Count },
                        DataLabels = true
                    });
                }
            }

            var assignGroups = assignSet
                .GroupBy(t => t.Assignee?.User?.Username ?? "Не назначен")
                .Select(g => new { Name = g.Key, Count = g.Count() });

            int assignSortIndex = 0;
            int topAssigneesIndex = 0;
            Dispatcher.Invoke(() =>
            {
                assignSortIndex = AssigneeSortCombo.SelectedIndex;
                topAssigneesIndex = TopAssigneesCombo.SelectedIndex;
            });

            if (assignSortIndex == 1)
                assignGroups = assignGroups.OrderBy(x => x.Name);
            else
                assignGroups = assignGroups.OrderByDescending(x => x.Count);

            if (topAssigneesIndex == 1)
                assignGroups = assignGroups.Take(5);

            var assignList = assignGroups.ToList();
            LabelsAssignees = [.. assignList.Select(x => x.Name)];

            SeriesByAssignee.Clear();

            if (assignList.Count == 0)
            {
                LabelsAssignees = ["Нет данных"];
                SeriesByAssignee.Add(new RowSeries
                {
                    Values = new ChartValues<int> { 0 },
                    DataLabels = false
                });
            }
            else
            {
                SeriesByAssignee.Add(new RowSeries
                {
                    Title = "Тикеты",
                    Values = new ChartValues<int>(assignList.Select(x => x.Count)),
                    DataLabels = true
                });
            }
        }

        private static string GetComboText(ComboBox box)
        {
            if (box.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? string.Empty;

            return box.SelectedItem?.ToString() ?? string.Empty;
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DashboardTicketsGrid.ItemsSource is not ICollection<Ticket> tickets || tickets.Count == 0)
                {
                    MessageBox.Show("Нет данных для экспорта.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter = "CSV файлы (*.csv)|*.csv",
                    FileName = $"Dashboard_{DateTime.Now:yyyyMMdd}.csv"
                };

                if (dlg.ShowDialog() == true)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("ID;Title;Status;Category;Priority;Assignee;CreatedAt;DueAt");

                    foreach (var t in tickets)
                    {
                        string safeTitle = t.Title.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");

                        sb.AppendLine(
                            $"{t.Id};\"{safeTitle}\";{t.Status};{t.Category};{t.Priority};" +
                            $"{t.Assignee?.User?.Username ?? "—"};{t.CreatedAt:yyyy-MM-dd HH:mm};{t.DueAt:yyyy-MM-dd HH:mm}");
                    }

                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

                    if (MessageBox.Show("CSV файл сохранен. Открыть?",
                            "Экспорт", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта в CSV: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
                Filter = "PDF Files|*.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    GeneratePdfReport(dlg.FileName);
                    MessageBox.Show("Отчет сохранен!", "Экспорт",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GeneratePdfReport(string filename)
        {
            using var document = new PdfDocument { Info = { Title = "Analytics Report" } };

            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var fontTitle = new XFont("Verdana", 16, XFontStyle.Bold);
            var fontHeader = new XFont("Verdana", 11, XFontStyle.Bold);
            var fontNormal = new XFont("Verdana", 10, XFontStyle.Regular);
            var fontSmall = new XFont("Verdana", 8, XFontStyle.Regular);

            gfx.DrawString("Аналитический отчет по нагрузке и KPI техподдержки", fontTitle, XBrushes.DarkBlue,
                new XRect(0, 20, page.Width, 40), XStringFormats.TopCenter);

            string periodStr = $"Период: {FromDatePicker.SelectedDate:dd.MM.yyyy} — {ToDatePicker.SelectedDate:dd.MM.yyyy}";
            gfx.DrawString(periodStr, fontNormal, XBrushes.Black, 40, 60);
            gfx.DrawString($"Сформировал: {CurrentUserName} ({CurrentUserRole})", fontNormal, XBrushes.Black, 40, 75);
            gfx.DrawString($"Дата выгрузки: {DateTime.Now:dd.MM.yyyy HH:mm}", fontNormal, XBrushes.Gray, 40, 90);

            double y = 130;

            DrawKpiBox(gfx, "Всего тикетов", KpiTotal.ToString(), 40, y);
            DrawKpiBox(gfx, "В работе", KpiInProgress.ToString(), 130, y);
            DrawKpiBox(gfx, "Закрыто", KpiClosed.ToString(), 220, y);
            DrawKpiBox(gfx, "Просрочено", KpiOverdue.ToString(), 310, y, XBrushes.Red);
            DrawKpiBox(gfx, "Решено (%)", KpiResolvedPercentText, 400, y);
            DrawKpiBox(gfx, "Ср. время", KpiAvgResolutionText, 490, y, XBrushes.DarkGreen);

            y += 50;
            gfx.DrawLine(XPens.LightGray, 40, y, page.Width - 40, y);
            y += 20;

            gfx.DrawString("Распределение нагрузки по сотрудникам:", fontHeader, XBrushes.Black, 40, y);

            if (FindName("ChartAssignees") is UIElement chartAssigneesControl)
            {
                var imgAssignees = CaptureControl(chartAssigneesControl);
                if (imgAssignees != null)
                {
                    gfx.DrawImage(imgAssignees, 40, y + 20, 500, 180);
                    y += 220;
                }
            }

            gfx.DrawString("Динамика поступления обращений:", fontHeader, XBrushes.Black, 40, y);

            if (FindName("ChartDynamics") is UIElement chartDynamicsControl)
            {
                var imgDynamics = CaptureControl(chartDynamicsControl);
                if (imgDynamics != null)
                {
                    gfx.DrawImage(imgDynamics, 40, y + 20, 500, 180);
                }
            }

            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            y = 40;

            gfx.DrawString("Распределение по категориям:", fontHeader, XBrushes.Black, 40, y);

            if (FindName("ChartCategories") is UIElement chartCatControl)
            {
                var imgCats = CaptureControl(chartCatControl);
                if (imgCats != null) gfx.DrawImage(imgCats, 40, y + 20, 230, 160);
            }

            if (FindName("ChartPriorities") is UIElement chartPrioControl)
            {
                var imgPrio = CaptureControl(chartPrioControl);
                if (imgPrio != null)
                {
                    gfx.DrawString("Распределение по приоритетам:", fontHeader, XBrushes.Black, 310, y);
                    gfx.DrawImage(imgPrio, 310, y + 20, 230, 160);
                }
            }

            y += 200;

            if (_userSettings == null || _userSettings.ShowDetailedTable)
            {
                gfx.DrawString("Детализация заявок (выборка):", fontHeader, XBrushes.Black, 40, y);
                y += 20;

                gfx.DrawRectangle(XBrushes.LightGray, 40, y - 12, page.Width - 80, 20);
                gfx.DrawString("ID", fontHeader, XBrushes.Black, 45, y);
                gfx.DrawString("Тема", fontHeader, XBrushes.Black, 80, y);
                gfx.DrawString("Статус", fontHeader, XBrushes.Black, 280, y);
                gfx.DrawString("Исполнитель", fontHeader, XBrushes.Black, 360, y);
                gfx.DrawString("Создано", fontHeader, XBrushes.Black, 460, y);

                y += 15;

                if (DashboardTicketsGrid.ItemsSource is IEnumerable<Ticket> tickets)
                {
                    foreach (var t in tickets)
                    {
                        if (y > page.Height - 50)
                        {
                            page = document.AddPage();
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;

                            gfx.DrawRectangle(XBrushes.LightGray, 40, y - 12, page.Width - 80, 20);
                            gfx.DrawString("ID", fontHeader, XBrushes.Black, 45, y);
                            gfx.DrawString("Тема", fontHeader, XBrushes.Black, 80, y);
                            gfx.DrawString("Статус", fontHeader, XBrushes.Black, 280, y);
                            gfx.DrawString("Исполнитель", fontHeader, XBrushes.Black, 360, y);
                            gfx.DrawString("Создано", fontHeader, XBrushes.Black, 460, y);
                            y += 15;
                        }

                        gfx.DrawString(t.Id.ToString(), fontSmall, XBrushes.Black, 45, y);

                        string title = t.Title.Length > 35
                            ? string.Concat(t.Title.AsSpan(0, 32), "...")
                            : t.Title;

                        gfx.DrawString(title, fontSmall, XBrushes.Black, 80, y);

                        var statusBrush = t.IsOverdue ? XBrushes.Red : (t.Status == Constants.TicketStatus.Closed ? XBrushes.DarkGreen : XBrushes.Black);
                        gfx.DrawString(t.Status, fontSmall, statusBrush, 280, y);

                        gfx.DrawString(t.Assignee?.User?.Username ?? "—", fontSmall, XBrushes.Black, 360, y);
                        gfx.DrawString(t.CreatedAt.ToString("dd.MM.yy"), fontSmall, XBrushes.Black, 460, y);

                        y += 15;
                        gfx.DrawLine(XPens.LightGray, 40, y - 10, page.Width - 40, y - 10);
                    }
                }
            }

            document.Save(filename);
        }

        private static XImage? CaptureControl(UIElement source)
        {
            try
            {
                double actualHeight = source.RenderSize.Height;
                double actualWidth = source.RenderSize.Width;

                if (actualHeight <= 0 || actualWidth <= 0)
                    return null;

                var renderTarget = new RenderTargetBitmap(
                    (int)actualWidth,
                    (int)actualHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32);

                renderTarget.Render(source);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                var bytes = stream.ToArray();

                return XImage.FromStream(() => new MemoryStream(bytes));
            }
            catch
            {
                return null;
            }
        }

        private static void DrawKpiBox(
            XGraphics gfx,
            string label,
            string value,
            double x,
            double y,
            XBrush? valueBrush = null)
        {
            var fontLabel = new XFont("Verdana", 8, XFontStyle.Regular);
            var fontValue = new XFont("Verdana", 12, XFontStyle.Bold);

            valueBrush ??= XBrushes.Black;

            gfx.DrawString(label, fontLabel, XBrushes.Gray, x, y);
            gfx.DrawString(value, fontValue, valueBrush, x, y + 12);
        }

        private void DashboardTicketsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DashboardTicketsGrid.SelectedItem is Ticket t)
            {
                var win = new TicketDetailsWindow(t.Id) { Owner = Window.GetWindow(this) };
                if (win.ShowDialog() == true)
                {
                    _ = LoadFromDbAsync();
                }
            }
        }

        private void ApplyFilters_Click(object sender, RoutedEventArgs e) => _ = LoadFromDbAsync();

        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            InitializeFilters();
            _ = LoadFromDbAsync();
        }

        private void Quick7_Click(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-7);
            _ = LoadFromDbAsync();
        }

        private void Quick30_Click(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-30);
            _ = LoadFromDbAsync();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) UpdateDashboard();
        }

        private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) UpdateDashboard();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class EmployeePerformanceVm
    {
        public string Name { get; set; } = string.Empty;
        public int ActiveTickets { get; set; }
        public int WorkloadPoints { get; set; }
        public double SlaPercentage { get; set; }
        public double ArtHours { get; set; }

        public string SlaText => $"{SlaPercentage:0.#}%";
        public string ArtText => $"{ArtHours:0.#}";

        public bool IsOverloaded => WorkloadPoints >= KpiService.MaxWorkloadPoints;
        public bool IsWarning => WorkloadPoints >= (KpiService.MaxWorkloadPoints * 0.75) && WorkloadPoints < KpiService.MaxWorkloadPoints;

        public string WorkloadStatus
        {
            get
            {
                if (IsOverloaded) return "Перегруз";
                if (IsWarning) return "Плотная";
                return "В норме";
            }
        }
    }
}