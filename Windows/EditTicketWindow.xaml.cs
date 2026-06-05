using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class EditTicketWindow : Window
    {
        private readonly AppDbContext _context;
        private readonly int _ticketId;
        private Ticket? _ticket;

        public EditTicketWindow(int ticketId, AppDbContext context)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _ticketId = ticketId;

            LoadComboBoxData();             LoadKnowledgeBase();
            LoadTicket();
        }

        private void LoadComboBoxData()
        {
            try
            {
                CategoryComboBox.ItemsSource = Enum.GetValues(typeof(TicketCategory));
                PriorityComboBox.ItemsSource = Enum.GetValues(typeof(TicketPriority));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading enums: {ex.Message}");
            }
        }

        private void LoadKnowledgeBase()
        {
            try
            {
                var articles = _context.KnowledgeBase
                    .AsNoTracking()
                    .OrderBy(k => k.Title)
                    .ToList();

                KnowledgeBaseBox.ItemsSource = articles;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading KB: {ex.Message}");
            }
        }

        private void LoadTicket()
        {
            _ticket = _context.Tickets
                .Include(t => t.Solution)
                .Include(t => t.Client)                 .FirstOrDefault(t => t.Id == _ticketId);

            if (_ticket != null)
            {
                                TicketIdBadge.Text = $"#{_ticket.Id}";
                TitleTextBox.Text = _ticket.Title;
                DescriptionTextBox.Text = _ticket.Description;
                StatusText.Text = _ticket.Status;
                CreatedAtText.Text = _ticket.CreatedAt.ToLocalTime().ToString("g");
                ClientNameText.Text = _ticket.Client?.Name ?? "Неизвестно";

                                CategoryComboBox.SelectedItem = _ticket.Category;
                PriorityComboBox.SelectedItem = _ticket.Priority;

                                if (_ticket.Solution != null &&
                    _ticket.Solution.KnowledgeArticleId is int articleId &&
                    KnowledgeBaseBox.Items.Count > 0)
                {
                    var selected = KnowledgeBaseBox.Items
                        .OfType<KnowledgeArticle>()
                        .FirstOrDefault(a => a.Id == articleId);

                    if (selected != null)
                        KnowledgeBaseBox.SelectedItem = selected;
                }
            }
            else
            {
                MessageBox.Show("Тикет не найден!");
                Close();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var ticket = _ticket;
            if (ticket == null) return;

            var newTitle = TitleTextBox.Text?.Trim() ?? string.Empty;
            var newDescription = DescriptionTextBox.Text?.Trim() ?? string.Empty;

            TitleError.Visibility = Visibility.Collapsed;
            DescriptionError.Visibility = Visibility.Collapsed;

            bool invalid = false;
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                TitleError.Text = "Тема не может быть пустой";
                TitleError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (string.IsNullOrWhiteSpace(newDescription))
            {
                DescriptionError.Text = "Описание не может быть пустым";
                DescriptionError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (invalid) return;

                        string history = "";

            if (ticket.Title != newTitle) history += "Изменена тема. ";
            if (ticket.Description != newDescription) history += "Изменено описание. ";

            if (CategoryComboBox.SelectedItem is TicketCategory newCat && ticket.Category != newCat)
            {
                history += $"Категория: {ticket.Category} -> {newCat}. ";
                ticket.Category = newCat;
            }

            if (PriorityComboBox.SelectedItem is TicketPriority newPrio && ticket.Priority != newPrio)
            {
                history += $"Приоритет: {ticket.Priority} -> {newPrio}. ";
                ticket.Priority = newPrio;
            }

            ticket.Title = newTitle;
            ticket.Description = newDescription;
            ticket.UpdatedAt = DateTime.UtcNow;

                        var selectedArticle = KnowledgeBaseBox.SelectedItem as KnowledgeArticle;
            int? oldArticleId = ticket.Solution?.KnowledgeArticleId;
            int? newArticleId = selectedArticle?.Id;

            if (newArticleId != oldArticleId)
            {
                history += "Изменена привязка к статье БЗ.";

                if (newArticleId == null)
                {
                                        if (ticket.Solution != null)
                    {
                        ticket.Solution.KnowledgeArticleId = null;
                    }
                }
                else
                {
                    if (ticket.Solution == null)
                    {
                        var solution = new Solution
                        {
                            TicketId = ticket.Id,
                            KnowledgeArticleId = newArticleId,
                            ResolutionDate = DateTime.UtcNow,
                            ResolutionText = selectedArticle?.Title ?? "Решение"
                        };
                        ticket.Solution = solution;
                        _context.Solutions.Add(solution);
                    }
                    else
                    {
                        ticket.Solution.KnowledgeArticleId = newArticleId;
                                                                    }
                }
            }

                        if (!string.IsNullOrWhiteSpace(history))
            {
                _context.TicketHistories.Add(new TicketHistory
                {
                    TicketId = ticket.Id,
                    Action = "Редактирование",
                    Details = history.Trim(),
                    Timestamp = DateTime.UtcNow
                });
            }

            try
            {
                _context.SaveChanges();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}