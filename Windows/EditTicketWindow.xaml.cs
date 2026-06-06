using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class EditTicketWindow : Window
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;
        private readonly int _ticketId;
        private Ticket? _ticket;

        // Права, вычисленные под текущего пользователя (используются в UI и при сохранении).
        private bool _canEditText;
        private bool _canEditCategory;

        public EditTicketWindow(int ticketId, AppDbContext context, AuthService authService)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _authService = authService ?? new AuthService(_context);
            _ticketId = ticketId;

            LoadComboBoxData();
            LoadTicket();
            ApplyRolePermissions();
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

        private void LoadTicket()
        {
            _ticket = _context.Tickets
                .Include(t => t.Client)
                .FirstOrDefault(t => t.Id == _ticketId);

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
            }
            else
            {
                MessageBox.Show("Тикет не найден!");
                Close();
            }
        }

        // Доступность полей строго по ролям (см. матрицу прав):
        //  - Тема/Описание: только клиент-владелец и пока тикет не закрыт;
        //  - Категория: только администратор;
        //  - Приоритет: не редактируется никем (только авто);
        //  - Привязка статьи БЗ: выполняется при решении тикета, не здесь.
        private void ApplyRolePermissions()
        {
            var user = _authService.CurrentUser;
            var role = user?.Role;
            bool isAdmin = Constants.UserRoles.IsAdmin(role);
            bool isClient = Constants.UserRoles.IsClient(role);
            bool isClosed = _ticket?.Status == Constants.TicketStatus.Closed;

            bool isOwnerClient = false;
            if (isClient && _ticket != null && user != null)
            {
                var ownClientId = _context.Clients.AsNoTracking()
                    .Where(c => c.UserId == user.Id)
                    .Select(c => (int?)c.Id)
                    .FirstOrDefault();
                isOwnerClient = ownClientId != null && _ticket.ClientId == ownClientId.Value;
            }

            _canEditText = isOwnerClient && !isClosed;
            _canEditCategory = isAdmin;

            TitleTextBox.IsReadOnly = !_canEditText;
            DescriptionTextBox.IsReadOnly = !_canEditText;
            CategoryComboBox.IsEnabled = _canEditCategory;

            // Приоритет — только чтение; привязка БЗ скрыта (выполняется при решении).
            PriorityComboBox.IsEnabled = false;
            KbBindingPanel.Visibility = Visibility.Collapsed;

            if (isAdmin)
                SubtitleText.Text = "Администратор может изменить только категорию обращения.";
            else if (_canEditText)
                SubtitleText.Text = "Вы можете изменить тему и описание своего обращения.";
            else
                SubtitleText.Text = "Изменение полей для вашей роли недоступно.";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var ticket = _ticket;
            if (ticket == null) return;

            string history = "";

            // Тема/Описание — только клиент-владелец незакрытого тикета.
            if (_canEditText)
            {
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

                if (ticket.Title != newTitle)
                {
                    history += "Изменена тема. ";
                    ticket.Title = newTitle;
                }
                if (ticket.Description != newDescription)
                {
                    history += "Изменено описание. ";
                    ticket.Description = newDescription;
                }
            }

            // Категория — только администратор.
            if (_canEditCategory && CategoryComboBox.SelectedItem is TicketCategory newCat && ticket.Category != newCat)
            {
                history += $"Категория: {ticket.Category} → {newCat}. ";
                ticket.Category = newCat;
            }

            // Приоритет вручную не меняется. Привязка статьи БЗ делается при решении тикета.

            if (string.IsNullOrWhiteSpace(history))
            {
                DialogResult = false;
                Close();
                return;
            }

            ticket.UpdatedAt = DateTime.UtcNow;
            _context.TicketHistories.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                Action = "Редактирование",
                Details = history.Trim(),
                Timestamp = DateTime.UtcNow
            });

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
