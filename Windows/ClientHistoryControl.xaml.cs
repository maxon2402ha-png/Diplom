using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ClientHistoryControl : UserControl
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;
        private int? _currentClientId;

        public ClientHistoryControl(AppDbContext context, AuthService authService)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            FromPicker.SelectedDate = DateTime.Now.AddMonths(-3);
            ToPicker.SelectedDate = DateTime.Now;

            LoadClients();
        }

        private void LoadClients()
        {
            var current = _authService.CurrentUser;
            if (current == null) return;

            if (current.Role == Constants.UserRoles.Client)
            {
                var client = _context.Clients
                    .AsNoTracking()
                    .FirstOrDefault(c => c.UserId == current.Id);

                if (client != null)
                {
                    ClientComboBox.ItemsSource = new[] { client };
                    ClientComboBox.SelectedItem = client;
                    ClientComboBox.IsEnabled = false;
                    _currentClientId = client.Id;
                    LoadTicketsForClient();
                }
            }
            else
            {
                var clients = _context.Clients
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .ToList();

                ClientComboBox.ItemsSource = clients;
                ClientComboBox.DisplayMemberPath = "Name";

                                if (clients.Count > 0)
                {
                    ClientComboBox.SelectedIndex = 0;
                }
            }
        }

        private void ClientComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClientComboBox.SelectedItem is Client client)
            {
                _currentClientId = client.Id;
                LoadTicketsForClient();
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e) => LoadTicketsForClient();

        private void LoadTicketsForClient()
        {
            if (_currentClientId == null) return;

            var clientId = _currentClientId.Value;

            try
            {
                if (ClientTicketsGrid == null) return;

                var query = _context.Tickets
                    .Include(t => t.Solution)
                    .Include(t => t.Client)
                    .Include(t => t.Feedback)
                    .Include(t => t.Assignee).ThenInclude(a => a!.User)
                    .AsNoTracking()
                    .Where(t => t.ClientId == clientId);

                if (FromPicker.SelectedDate.HasValue)
                {
                    var from = FromPicker.SelectedDate.Value.Date.ToUniversalTime();
                    query = query.Where(t => t.CreatedAt >= from);
                }

                if (ToPicker.SelectedDate.HasValue)
                {
                    var to = ToPicker.SelectedDate.Value.Date.AddDays(1).ToUniversalTime();
                    query = query.Where(t => t.CreatedAt < to);
                }

                if (ClosedOnlyCheckBox?.IsChecked == true)
                    query = query.Where(t => t.Status == Constants.TicketStatus.Closed);

                var tickets = query
                    .OrderByDescending(t => t.CreatedAt)
                    .ToList();

                ClientTicketsGrid.ItemsSource = tickets;

                UpdateStats(tickets);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}");
            }
        }

        private void UpdateStats(List<Ticket> tickets)
        {
            int total = tickets.Count;
            int closed = tickets.Count(t => t.Status == Constants.TicketStatus.Closed);
            int inProgress = total - closed;

                        TotalCountText.Text = total.ToString();
            OpenCountText.Text = inProgress.ToString();
            ClosedCountText.Text = closed.ToString();

            var resolvedTickets = tickets
                .Where(t => t.Status == Constants.TicketStatus.Closed && t.ClosedAt.HasValue)
                .ToList();

                        if (resolvedTickets.Count > 0)
            {
                double avgHours = resolvedTickets.Average(t => (t.ClosedAt!.Value - t.CreatedAt).TotalHours);

                if (avgHours < 24)
                    AvgResolutionText.Text = $"{avgHours:0.#} ч";
                else
                    AvgResolutionText.Text = $"{(avgHours / 24):0.#} дн";
            }
            else
            {
                AvgResolutionText.Text = "—";
            }

            var ratedTickets = tickets.Where(t => t.Feedback != null).ToList();

                        if (ratedTickets.Count > 0)
            {
                double avgRating = ratedTickets.Average(t => t.Feedback!.Rating);
                AvgRatingText.Text = $"{avgRating:0.0} / 5";
            }
            else
            {
                AvgRatingText.Text = "—";
            }
        }

                private void ExportClientCsvButton_Click(object sender, RoutedEventArgs e)
        {
                        if (ClientTicketsGrid.ItemsSource is not ICollection<Ticket> tickets || tickets.Count == 0)
            {
                MessageBox.Show("Нет данных.");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"History_{DateTime.Now:yyyyMMdd}.csv"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("ID;Тема;Статус;Дата;Решение");

                    foreach (var t in tickets)
                    {
                        string res = t.Solution?.ResolutionText?.Replace(";", ",") ?? string.Empty;
                        sb.AppendLine($"{t.Id};{t.Title};{t.Status};{t.CreatedAt};{res}");
                    }

                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

                    if (MessageBox.Show("Файл создан. Открыть?", "Экспорт",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

                private void ExportClientPdfButton_Click(object sender, RoutedEventArgs e)
        {
                        if (ClientTicketsGrid.ItemsSource is not ICollection<Ticket> tickets || tickets.Count == 0)
            {
                MessageBox.Show("Нет данных.");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"History_{DateTime.Now:yyyyMMdd}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using var document = new PdfDocument();
                    document.Info.Title = "История клиента";

                    var page = document.AddPage();
                    var gfx = XGraphics.FromPdfPage(page);

                    var fontTitle = new XFont("Verdana", 16, XFontStyle.Bold);
                    var fontHeader = new XFont("Verdana", 10, XFontStyle.Bold);
                    var fontRow = new XFont("Verdana", 10, XFontStyle.Regular);

                    double y = 40;
                    gfx.DrawString("История обращений", fontTitle, XBrushes.DarkBlue, 40, y);
                    y += 40;

                    gfx.DrawString("#", fontHeader, XBrushes.Black, 40, y);
                    gfx.DrawString("Тема", fontHeader, XBrushes.Black, 80, y);
                    gfx.DrawString("Статус", fontHeader, XBrushes.Black, 300, y);
                    gfx.DrawString("Дата", fontHeader, XBrushes.Black, 400, y);

                    y += 20;
                    gfx.DrawLine(XPens.Gray, 40, y, page.Width - 40, y);
                    y += 10;

                    foreach (var t in tickets)
                    {
                        if (y > page.Height - 40)
                        {
                            page = document.AddPage();
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }

                        gfx.DrawString(t.Id.ToString(), fontRow, XBrushes.Black, 40, y);

                                                string title = t.Title.Length > 35
                            ? string.Concat(t.Title.AsSpan(0, 32), "...")
                            : t.Title;

                        gfx.DrawString(title, fontRow, XBrushes.Black, 80, y);

                        gfx.DrawString(t.Status, fontRow, XBrushes.Black, 300, y);
                        gfx.DrawString(t.CreatedAt.ToString("dd.MM.yyyy"), fontRow, XBrushes.Black, 400, y);

                        y += 20;
                    }

                    document.Save(dlg.FileName);

                    if (MessageBox.Show("PDF создан. Открыть?", "Экспорт",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
                        if (Window.GetWindow(this) is MainWindow window)
            {
                window.OpenTickets_Click(sender, e);
            }
        }

        private void ClientTicketsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ClientTicketsGrid.SelectedItem is Ticket selected)
            {
                var window = Window.GetWindow(this);
                var detailsWindow = new TicketDetailsWindow(selected, _context, _authService)
                {
                    Owner = window
                };

                detailsWindow.ShowDialog();
                LoadTicketsForClient();
            }
        }
    }
}