using ClosedXML.Excel;
using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ReportsControl : UserControl
    {
        public string[] DynamicsLabels { get; private set; } = Array.Empty<string>();
        public string[] CategoryLabels { get; private set; } = Array.Empty<string>();
        public string[] OperatorLabels { get; private set; } = Array.Empty<string>();
        public string[] DayLabels { get; private set; } = Array.Empty<string>();

        private List<ReportRow> _currentRows = new();

        public ReportsControl()
        {
            InitializeComponent();
            DataContext = this;
            InitializeFilters();
            Loaded += async (_, _) => await LoadAsync();
        }

        private void InitializeFilters()
        {
            ToDatePicker.SelectedDate = DateTime.Today;
            FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);

            StatusFilter.Items.Add(new ComboBoxItem { Content = "Все", IsSelected = true });
            StatusFilter.Items.Add(new ComboBoxItem { Content = Constants.TicketStatus.Open });
            StatusFilter.Items.Add(new ComboBoxItem { Content = Constants.TicketStatus.InProgress });
            StatusFilter.Items.Add(new ComboBoxItem { Content = Constants.TicketStatus.Resolved });
            StatusFilter.Items.Add(new ComboBoxItem { Content = Constants.TicketStatus.Closed });

            PriorityFilter.Items.Add(new ComboBoxItem { Content = "Все", IsSelected = true });
            foreach (var p in Enum.GetValues<TicketPriority>())
                PriorityFilter.Items.Add(new ComboBoxItem { Content = p.ToString() });
        }

        private async void Apply_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            try
            {
                var fromUtc = (FromDatePicker.SelectedDate?.Date ?? DateTime.Today.AddDays(-30)).ToUniversalTime();
                var toUtc = ((ToDatePicker.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddSeconds(-1)).ToUniversalTime();

                using var db = App.CreateDbContext();

                var query = db.Tickets
                    .Include(t => t.Assignee).ThenInclude(a => a!.User)
                    .Include(t => t.Client)
                    .AsNoTracking()
                    .Where(t => t.CreatedAt >= fromUtc && t.CreatedAt <= toUtc);

                if (StatusFilter.SelectedItem is ComboBoxItem si && si.Content?.ToString() is { } st && st != "Все")
                    query = query.Where(t => t.Status == st);

                if (PriorityFilter.SelectedItem is ComboBoxItem pi && pi.Content?.ToString() is { } pStr
                    && pStr != "Все" && Enum.TryParse<TicketPriority>(pStr, out var pVal))
                    query = query.Where(t => t.Priority == pVal);

                var list = await query.ToListAsync();

                _currentRows = list.Select(t => new ReportRow
                {
                    Id = t.Id,
                    Title = t.Title,
                    Status = t.Status,
                    Priority = t.Priority.ToString(),
                    Category = t.Category.ToString(),
                    CreatedAt = t.CreatedAt,
                    ClosedAt = t.ClosedAt,
                    DueAt = t.DueAt,
                    AssigneeName = t.Assignee?.User?.Username ?? "—",
                    ClientName = t.Client?.Name ?? "—",
                    IsOverdue = t.DueAt.HasValue && t.DueAt < DateTime.UtcNow && t.Status != Constants.TicketStatus.Closed
                }).ToList();

                UpdateSummary();
                UpdateDynamics();
                UpdateCategoryChart();
                UpdateSlaChart();
                UpdateHeatmap();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка построения отчёта: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            TotalText.Text = _currentRows.Count.ToString();
            ClosedText.Text = _currentRows.Count(r => r.Status == Constants.TicketStatus.Closed).ToString();
            ActiveText.Text = _currentRows.Count(r => r.Status == Constants.TicketStatus.InProgress
                                                   || r.Status == Constants.TicketStatus.Open).ToString();
            OverdueText.Text = _currentRows.Count(r => r.IsOverdue).ToString();
        }

        private void UpdateDynamics()
        {
            var fromDate = (FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-30)).Date;
            var toDate = (ToDatePicker.SelectedDate ?? DateTime.Today).Date;

            var days = new List<DateTime>();
            for (var d = fromDate; d <= toDate; d = d.AddDays(1)) days.Add(d);

            DynamicsLabels = days.Select(d => d.ToString("dd.MM")).ToArray();

            var createdByDay = days.Select(d =>
                _currentRows.Count(r => r.CreatedAt.ToLocalTime().Date == d)).ToList();
            var closedByDay = days.Select(d =>
                _currentRows.Count(r => r.ClosedAt.HasValue && r.ClosedAt.Value.ToLocalTime().Date == d)).ToList();

            DynamicsChart.Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Создано",
                    Values = new ChartValues<int>(createdByDay),
                    LineSmoothness = 0.4
                },
                new LineSeries
                {
                    Title = "Закрыто",
                    Values = new ChartValues<int>(closedByDay),
                    LineSmoothness = 0.4
                }
            };
            if (DynamicsChart.AxisX.Count > 0)
                DynamicsChart.AxisX[0].Labels = DynamicsLabels;
        }

        private void UpdateCategoryChart()
        {
            var groups = _currentRows
                .GroupBy(r => r.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            CategoryLabels = groups.Select(g => g.Category).ToArray();

            CategoryChart.Series = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "Тикетов",
                    Values = new ChartValues<int>(groups.Select(g => g.Count))
                }
            };
            if (CategoryChart.AxisX.Count > 0)
                CategoryChart.AxisX[0].Labels = CategoryLabels;
        }

        private void UpdateSlaChart()
        {
            var groups = _currentRows
                .Where(r => r.AssigneeName != "—")
                .GroupBy(r => r.AssigneeName)
                .Select(g => new
                {
                    Name = g.Key,
                    Total = g.Count(),
                    Overdue = g.Count(r => r.IsOverdue)
                })
                .OrderByDescending(g => g.Overdue)
                .Take(10)
                .ToList();

            OperatorLabels = groups.Select(g => g.Name).ToArray();

            SlaChart.Series = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "Всего",
                    Values = new ChartValues<int>(groups.Select(g => g.Total))
                },
                new ColumnSeries
                {
                    Title = "Просрочено",
                    Values = new ChartValues<int>(groups.Select(g => g.Overdue))
                }
            };
            if (SlaChart.AxisX.Count > 0)
                SlaChart.AxisX[0].Labels = OperatorLabels;
        }

        private void UpdateHeatmap()
        {
            var dayNames = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
            var byDay = new int[7];

            foreach (var r in _currentRows)
            {
                var local = r.CreatedAt.ToLocalTime();
                int idx = local.DayOfWeek switch
                {
                    DayOfWeek.Monday => 0,
                    DayOfWeek.Tuesday => 1,
                    DayOfWeek.Wednesday => 2,
                    DayOfWeek.Thursday => 3,
                    DayOfWeek.Friday => 4,
                    DayOfWeek.Saturday => 5,
                    _ => 6
                };
                byDay[idx]++;
            }

            DayLabels = dayNames;
            HeatmapChart.Series = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "Тикетов создано",
                    Values = new ChartValues<int>(byDay)
                }
            };
            if (HeatmapChart.AxisX.Count > 0)
                HeatmapChart.AxisX[0].Labels = DayLabels;
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRows.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта.", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Excel|*.xlsx",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("Тикеты");

                ws.Cell(1, 1).Value = "ID";
                ws.Cell(1, 2).Value = "Тема";
                ws.Cell(1, 3).Value = "Статус";
                ws.Cell(1, 4).Value = "Приоритет";
                ws.Cell(1, 5).Value = "Категория";
                ws.Cell(1, 6).Value = "Создан";
                ws.Cell(1, 7).Value = "Дедлайн";
                ws.Cell(1, 8).Value = "Закрыт";
                ws.Cell(1, 9).Value = "Клиент";
                ws.Cell(1, 10).Value = "Исполнитель";
                ws.Cell(1, 11).Value = "Просрочен";

                ws.Range(1, 1, 1, 11).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 11).Style.Fill.BackgroundColor = XLColor.LightGray;

                int row = 2;
                foreach (var r in _currentRows)
                {
                    ws.Cell(row, 1).Value = r.Id;
                    ws.Cell(row, 2).Value = r.Title;
                    ws.Cell(row, 3).Value = r.Status;
                    ws.Cell(row, 4).Value = r.Priority;
                    ws.Cell(row, 5).Value = r.Category;
                    ws.Cell(row, 6).Value = r.CreatedAt.ToLocalTime();
                    ws.Cell(row, 7).Value = r.DueAt?.ToLocalTime();
                    ws.Cell(row, 8).Value = r.ClosedAt?.ToLocalTime();
                    ws.Cell(row, 9).Value = r.ClientName;
                    ws.Cell(row, 10).Value = r.AssigneeName;
                    ws.Cell(row, 11).Value = r.IsOverdue ? "Да" : "Нет";
                    row++;
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                MessageBox.Show($"Сохранено: {dlg.FileName}", "Готово",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта в Excel: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRows.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта.", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var doc = new PdfDocument();
                doc.Info.Title = "Отчёт по тикетам";

                var page = doc.AddPage();
                page.Size = PdfSharpCore.PageSize.A4;
                var gfx = XGraphics.FromPdfPage(page);

                var titleFont = new XFont("Arial", 16, XFontStyle.Bold);
                var bodyFont = new XFont("Arial", 10, XFontStyle.Regular);
                var headerFont = new XFont("Arial", 10, XFontStyle.Bold);

                gfx.DrawString("Отчёт по тикетам", titleFont, XBrushes.Black, new XPoint(40, 50));
                gfx.DrawString($"Период: {FromDatePicker.SelectedDate:dd.MM.yyyy} — {ToDatePicker.SelectedDate:dd.MM.yyyy}",
                    bodyFont, XBrushes.Gray, new XPoint(40, 75));
                gfx.DrawString($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm}", bodyFont, XBrushes.Gray, new XPoint(40, 95));

                gfx.DrawString($"Всего тикетов: {_currentRows.Count}", bodyFont, XBrushes.Black, new XPoint(40, 125));
                gfx.DrawString($"Закрыто: {_currentRows.Count(r => r.Status == Constants.TicketStatus.Closed)}",
                    bodyFont, XBrushes.Black, new XPoint(40, 145));
                gfx.DrawString($"Просрочено: {_currentRows.Count(r => r.IsOverdue)}",
                    bodyFont, XBrushes.Black, new XPoint(40, 165));

                double y = 200;
                gfx.DrawString("ID", headerFont, XBrushes.Black, new XPoint(40, y));
                gfx.DrawString("Тема", headerFont, XBrushes.Black, new XPoint(80, y));
                gfx.DrawString("Статус", headerFont, XBrushes.Black, new XPoint(330, y));
                gfx.DrawString("Приоритет", headerFont, XBrushes.Black, new XPoint(400, y));
                gfx.DrawString("Создан", headerFont, XBrushes.Black, new XPoint(480, y));
                y += 5;
                gfx.DrawLine(XPens.Gray, 40, y, 555, y);
                y += 12;

                PdfPage currentPage = page;
                XGraphics currentGfx = gfx;

                foreach (var r in _currentRows.Take(500))
                {
                    if (y > 780)
                    {
                        currentPage = doc.AddPage();
                        currentPage.Size = PdfSharpCore.PageSize.A4;
                        currentGfx = XGraphics.FromPdfPage(currentPage);
                        y = 50;
                    }
                    currentGfx.DrawString(r.Id.ToString(), bodyFont, XBrushes.Black, new XPoint(40, y));
                    var title = r.Title.Length > 40 ? r.Title.Substring(0, 40) + "…" : r.Title;
                    currentGfx.DrawString(title, bodyFont, XBrushes.Black, new XPoint(80, y));
                    currentGfx.DrawString(r.Status, bodyFont, XBrushes.Black, new XPoint(330, y));
                    currentGfx.DrawString(r.Priority, bodyFont, XBrushes.Black, new XPoint(400, y));
                    currentGfx.DrawString(r.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy"),
                        bodyFont, XBrushes.Black, new XPoint(480, y));
                    y += 16;
                }

                doc.Save(dlg.FileName);

                MessageBox.Show($"Сохранено: {dlg.FileName}", "Готово",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта в PDF: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class ReportRow
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Priority { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime? DueAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public string AssigneeName { get; set; } = string.Empty;
            public string ClientName { get; set; } = string.Empty;
            public bool IsOverdue { get; set; }
        }
    }
}
