using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.Versioning;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Helpers;
using КР_Ханников.Services;
using Constants = КР_Ханников.Core.Constants;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;
        private readonly NotificationService _notificationService;
        private readonly TicketService _ticketService;
        private DispatcherTimer? _notificationTimer;

        // Для отмены предыдущего запроса поиска при быстром вводе
        private CancellationTokenSource? _searchCts;
        private bool _isLoading = false;

        // Пагинация
        private int _currentPage = 1;
        private int _pageSize = 25;
        private int _totalTickets = 0;
        private bool _filterByCurrentUserLast = false;
        private string? _searchQueryLast = null;

        public User? CurrentUser => _authService?.CurrentUser;

        public ICommand NewTicketCommand { get; private set; } = null!;
        public ICommand FocusSearchCommand { get; private set; } = null!;
        public ICommand OpenKnowledgeBaseCommand { get; private set; } = null!;
        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand OpenSettingsCommand { get; private set; } = null!;
        public ICommand OpenGlobalSearchCommand { get; private set; } = null!;

        private void InitializeHotkeyCommands()
        {
            NewTicketCommand = new RelayCommand(() => CreateTicket_Click(this, new RoutedEventArgs()));

            FocusSearchCommand = new RelayCommand(() =>
            {
                if (SearchBox != null)
                {
                    SearchBox.Focus();
                    System.Windows.Input.Keyboard.Focus(SearchBox);
                    SearchBox.SelectAll();
                }
            });

            OpenKnowledgeBaseCommand = new RelayCommand(() =>
            {
                // База знаний доступна всем ролям (клиент видит только опубликованное).
                OpenKnowledgeBase_Click(this, new RoutedEventArgs());
            });

            RefreshCommand = new RelayCommand(() =>
            {
                bool isClient = CurrentUser?.Role == Constants.UserRoles.Client;
                _ = LoadTicketsAsync(isClient);
            });

            OpenSettingsCommand = new RelayCommand(() =>
                OpenNotificationSettings_Click(this, new RoutedEventArgs()));

            OpenGlobalSearchCommand = new RelayCommand(() =>
            {
                var wnd = new GlobalSearchWindow(_authService) { Owner = this };
                wnd.ShowDialog();
            });
        }

        public MainWindow(AppDbContext context, AuthService authService)
        {
            InitializeComponent();

            _context = context ?? throw new ArgumentNullException(nameof(context));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _notificationService = new NotificationService(_context, _authService);
            _ticketService = new TicketService(_context);

            Debug.WriteLine($"[MainWindow] Инициализация для: {_authService.CurrentUser?.Username}");

            InitializeFilters();
            InitializeHotkeyCommands();
            DataContext = this;
            ConfigureAccess();
            ApplySavedTheme();

            bool isClient = CurrentUser?.Role == Constants.UserRoles.Client;
            if (isClient) UpdateSidebar(MyTicketsButton); else UpdateSidebar(AllTicketsButton);

            LoadSavedSearches();
            StartTimers();

            // Первичная загрузка данных выполняется последовательно после
            // отрисовки окна. Это важно: AppDbContext не поддерживает несколько
            // одновременных запросов. Если запускать LoadTicketsAsync "в фоне"
            // (через _ = ...) и тут же синхронно дёргать контекст в
            // ShowUnreadNotificationsForCurrentUser, EF Core выбрасывает
            // "A command is already in progress". Поэтому сначала ждём загрузку
            // тикетов (await), и только потом обращаемся к уведомлениям.
            Loaded += async (s, e) =>
            {
                try
                {
                    await LoadTicketsAsync(isClient);
                    _notificationService.ShowUnreadNotificationsForCurrentUser();
                    UpdateNotificationsButtonCaption();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Ошибка первичной загрузки: {ex.Message}");
                }
            };
        }

        private void InitializeFilters()
        {
            if (CategoryFilter != null)
            {
                var cats = new List<object> { "Все" };
                cats.AddRange(Enum.GetValues(typeof(TicketCategory)).Cast<object>());
                CategoryFilter.ItemsSource = cats;
                CategoryFilter.SelectedIndex = 0;
            }

            if (PriorityFilter != null)
            {
                var prios = new List<object> { "Все" };
                prios.AddRange(Enum.GetValues(typeof(TicketPriority)).Cast<object>());
                PriorityFilter.ItemsSource = prios;
                PriorityFilter.SelectedIndex = 0;
            }
        }

        private void StartTimers()
        {
            _notificationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Constants.UI.NotificationCheckIntervalMinutes)
            };
            _notificationTimer.Tick += async (s, e) =>
            {
                try
                {
                    _notificationService.CheckDueSoonTicketsForCurrentUser();
                    await _notificationService.CheckWorkloadAlertsForCurrentUserAsync();
                    UpdateNotificationsButtonCaption();
                }
                catch { }
            };
            _notificationTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _notificationTimer?.Stop();
            base.OnClosed(e);
        }

        private void UpdateSidebar(Button? activeButton)
        {
            var buttons = new[] {
                OpenDashboardButton, OpenAnalyticsButton, MyTicketsButton, AllTicketsButton,
                ClientHistoryButton, ClosedTicketsButton, EmployeesButton,
                AuditButton, OpenKnowledgeBaseButton
            };

            foreach (var btn in buttons)
            {
                if (btn != null) btn.Style = (Style)FindResource("NavButton");
            }

            if (activeButton != null) activeButton.Style = (Style)FindResource("NavButtonActive");
        }

        private void SwitchPage(Button btn, UserControl content)
        {
            UpdateSidebar(btn);
            if (TicketsContent != null) TicketsContent.Visibility = Visibility.Collapsed;
            if (PagesContent != null)
            {
                PagesContent.Visibility = Visibility.Visible;
                PagesContent.Content = content;
            }
        }

        // --- Обработчики меню (навигация) ---
        private void OpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            var role = _authService.CurrentUser?.Role;
            // Admin — общий дашборд по системе; Support — личный KPI-дашборд оператора.
            if (Constants.UserRoles.IsAdmin(role))
                SwitchPage(OpenDashboardButton, new DashboardControl(_authService));
            else if (Constants.UserRoles.IsSupport(role))
                SwitchPage(OpenDashboardButton, new AgentDashboardControl(_authService));
            else
                MessageBox.Show("Доступ запрещен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Раздел аналитики KPI и нагрузки. Это UserControl, поэтому
        // встраивается в главное окно через SwitchPage, как и Дашборд.
        private void OpenAnalytics_Click(object sender, RoutedEventArgs e)
            => SwitchPage(OpenAnalyticsButton, new AnalyticsControl());

        private void OpenKnowledgeBase_Click(object sender, RoutedEventArgs e)
            => SwitchPage(OpenKnowledgeBaseButton, new KnowledgeBaseControl(_context, _authService));

        private void OpenClientHistory_Click(object sender, RoutedEventArgs e)
            => SwitchPage(ClientHistoryButton, new ClientHistoryControl(_context, _authService));

        private void OpenEmployees_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser?.Role == Constants.UserRoles.Admin)
                SwitchPage(EmployeesButton, new ManageEmployeesControl(_authService));
            else
                MessageBox.Show("Доступ запрещен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenAudit_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser?.Role == Constants.UserRoles.Admin)
                SwitchPage(AuditButton, new AuditLogControl(_context, _authService));
            else
                MessageBox.Show("Доступ запрещен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenReports_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser?.Role == Constants.UserRoles.Admin)
                SwitchPage(ReportsButton, new ReportsControl());
            else
                MessageBox.Show("Доступ запрещен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenMlMetrics_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser?.Role == Constants.UserRoles.Admin)
                SwitchPage(MlMetricsButton, new MlMetricsControl());
            else
                MessageBox.Show("Доступ запрещен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser?.Role == Constants.UserRoles.Admin)
                SwitchPage(BackupButton, new BackupManagerControl());
            else
                MessageBox.Show("Доступ запрещен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public async void OpenTickets_Click(object sender, RoutedEventArgs e)
        {
            bool isClient = CurrentUser?.Role == Constants.UserRoles.Client;
            var btn = isClient ? MyTicketsButton : AllTicketsButton;
            UpdateSidebar(btn);

            if (TicketsContent != null) TicketsContent.Visibility = Visibility.Visible;
            if (PagesContent != null)
            {
                PagesContent.Visibility = Visibility.Collapsed;
                PagesContent.Content = null;
            }
            await LoadTicketsAsync(isClient);
        }

        private async void OpenMyTickets_Click(object sender, RoutedEventArgs e)
        {
            UpdateSidebar(MyTicketsButton);
            ShowTickets();
            await LoadTicketsAsync(true);
        }

        private async void OpenAllTickets_Click(object sender, RoutedEventArgs e)
        {
            UpdateSidebar(AllTicketsButton);
            ShowTickets();
            await LoadTicketsAsync(false);
        }

        private async void OpenClosedTickets_Click(object sender, RoutedEventArgs e)
        {
            UpdateSidebar(ClosedTicketsButton);
            ShowTickets();

            if (StatusFilter != null)
            {
                foreach (ComboBoxItem item in StatusFilter.Items)
                {
                    if (item.Content?.ToString() == "Closed")
                    {
                        StatusFilter.SelectedItem = item;
                        break;
                    }
                }
            }
            await LoadTicketsAsync(false);
        }

        private void ShowTickets()
        {
            if (TicketsContent != null) TicketsContent.Visibility = Visibility.Visible;
            if (PagesContent != null) PagesContent.Visibility = Visibility.Collapsed;
        }

        // --- ЛОГИКА ЗАГРУЗКИ (АСИНХРОННАЯ) ---
        public async Task LoadTicketsAsync(bool filterByCurrentUser = false, string? searchQuery = null)
        {
            // Контролы с начальным выбором (например PageSizeCombo с IsSelected="True")
            // вызывают свои SelectionChanged-обработчики ещё во время InitializeComponent(),
            // т.е. до присвоения _context в конструкторе. Пропускаем такой преждевременный
            // вызов — первичная загрузка всё равно выполнится в обработчике Loaded.
            if (_context == null) return;

            if (_isLoading) return;
            _isLoading = true;
            TicketsLoadingOverlay?.Show("Загрузка тикетов...", "Подождите, идёт получение данных.");

            try
            {
                // Очистка трекера для получения свежих данных
                _context.ChangeTracker.Clear();

                var currentUser = _authService.CurrentUser;
                if (currentUser == null) return;

                // Строим запрос
                var query = _context.Tickets
                    .Include(t => t.Client)
                    .Include(t => t.Assignee).ThenInclude(a => a != null ? a.User : null)
                    .Include(t => t.Solution)
                    .AsNoTracking()
                    .AsQueryable();

                // Фильтрация по правам доступа
                if (currentUser.Role == Constants.UserRoles.Client)
                {
                    var client = await _context.Clients.FirstOrDefaultAsync(c => c.UserId == currentUser.Id);
                    if (client != null) query = query.Where(t => t.ClientId == client.Id);
                    else query = query.Where(t => false); // Нет клиента - нет тикетов
                }
                else if (currentUser.Role == Constants.UserRoles.Support && filterByCurrentUser)
                {
                    var empId = await GetCurrentEmployeeIdOrNullAsync();
                    query = query.Where(t => t.AssigneeEmployeeId == empId);
                }

                // Поиск
                var effectiveQuery = searchQuery ?? SearchBox?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(effectiveQuery))
                {
                    var term = effectiveQuery.ToLower();
                    query = query.Where(t =>
                        (t.Title != null && t.Title.ToLower().Contains(term)) ||
                        (t.Description != null && t.Description.ToLower().Contains(term)) ||
                        (t.Solution != null && t.Solution.ResolutionText != null && t.Solution.ResolutionText.ToLower().Contains(term)) ||
                        (t.Client != null && t.Client.Name.ToLower().Contains(term)) ||
                        t.Id.ToString().Contains(term));
                }

                // Фильтры UI
                if (StatusFilter?.SelectedItem is ComboBoxItem sel && sel.Content is string st && st != "Все статусы" && st != "Все")
                {
                    if (st == "In Progress") query = query.Where(t => t.Status == Constants.TicketStatus.InProgress);
                    else query = query.Where(t => t.Status == st);
                }

                if (CategoryFilter?.SelectedItem is TicketCategory cat)
                    query = query.Where(t => t.Category == cat);

                if (PriorityFilter?.SelectedItem is TicketPriority pr)
                    query = query.Where(t => t.Priority == pr);

                if (CreatedFromPicker?.SelectedDate is DateTime from)
                    query = query.Where(t => t.CreatedAt >= from.Date.ToUniversalTime());

                if (CreatedToPicker?.SelectedDate is DateTime to)
                    query = query.Where(t => t.CreatedAt < to.Date.AddDays(1).ToUniversalTime());

                // Запоминаем последние параметры для пагинации
                _filterByCurrentUserLast = filterByCurrentUser;
                _searchQueryLast = searchQuery;

                // Получаем общий счёт ДО Skip/Take
                _totalTickets = await query.CountAsync();

                // Корректируем текущую страницу если она вышла за пределы
                var totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalTickets / _pageSize));
                if (_currentPage > totalPages) _currentPage = totalPages;
                if (_currentPage < 1) _currentPage = 1;

                // Сортировка + Skip/Take
                var tickets = await query
                    .OrderByDescending(t => t.Priority)
                    .ThenByDescending(t => t.CreatedAt)
                    .Skip((_currentPage - 1) * _pageSize)
                    .Take(_pageSize)
                    .ToListAsync();

                if (TicketsGrid != null) TicketsGrid.ItemsSource = tickets;
                if (TicketCountText != null) TicketCountText.Text = $"{_totalTickets} заявок";

                UpdatePaginationUI(totalPages);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                TicketsLoadingOverlay?.Hide();
            }
        }

        private void UpdatePaginationUI(int totalPages)
        {
            if (PageNumberText != null)
                PageNumberText.Text = $"{_currentPage} / {totalPages}";

            if (PaginationInfoText != null)
            {
                if (_totalTickets == 0)
                {
                    PaginationInfoText.Text = "Нет результатов";
                }
                else
                {
                    var from = (_currentPage - 1) * _pageSize + 1;
                    var to = Math.Min(_currentPage * _pageSize, _totalTickets);
                    PaginationInfoText.Text = $"Показано {from}–{to} из {_totalTickets}";
                }
            }

            if (PrevPageButton != null) PrevPageButton.IsEnabled = _currentPage > 1;
            if (NextPageButton != null) NextPageButton.IsEnabled = _currentPage < totalPages;
        }

        private async void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage <= 1) return;
            _currentPage--;
            await LoadTicketsAsync(_filterByCurrentUserLast, _searchQueryLast);
        }

        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalTickets / _pageSize));
            if (_currentPage >= totalPages) return;
            _currentPage++;
            await LoadTicketsAsync(_filterByCurrentUserLast, _searchQueryLast);
        }

        private async void PageSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageSizeCombo?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString(), out var size))
            {
                _pageSize = size;
                _currentPage = 1;
                await LoadTicketsAsync(_filterByCurrentUserLast, _searchQueryLast);
            }
        }

        // Обработка ввода с задержкой (Debounce)
        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (MyTicketsButton == null) return;
            bool myOnly = MyTicketsButton.Style == (Style)FindResource("NavButtonActive");

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            try
            {
                await Task.Delay(500, token); // Ждем 500мс
                _currentPage = 1; // сброс на первую страницу при изменении поиска
                await LoadTicketsAsync(myOnly, SearchBox?.Text?.Trim());
            }
            catch (TaskCanceledException) { /* Игнорируем отмену */ }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
            => await LoadTicketsAsync(searchQuery: SearchBox?.Text?.Trim());

        private async void CreateTicket_Click(object sender, RoutedEventArgs e)
        {
            if (new CreateTicketWindow(_authService).ShowDialog() == true)
            {
                if (MyTicketsButton == null) return;
                bool myOnly = MyTicketsButton.Style == (Style)FindResource("NavButtonActive");
                await LoadTicketsAsync(myOnly);
            }
        }

        private async void EditTicket_Click(object sender, RoutedEventArgs e)
        {
            if (TicketsGrid?.SelectedItem is Ticket s)
            {
                var detailsWindow = new TicketDetailsWindow(s, _context, _authService) { Owner = this };
                detailsWindow.ShowDialog();
                if (MyTicketsButton == null) return;
                bool myOnly = MyTicketsButton.Style == (Style)FindResource("NavButtonActive");
                await LoadTicketsAsync(myOnly);
            }
        }

        private async void TicketsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TicketsGrid?.SelectedItem is Ticket s)
            {
                var detailsWindow = new TicketDetailsWindow(s, _context, _authService) { Owner = this };
                detailsWindow.ShowDialog();
                if (MyTicketsButton == null) return;
                bool myOnly = MyTicketsButton.Style == (Style)FindResource("NavButtonActive");
                await LoadTicketsAsync(myOnly);
            }
        }

        private async void AssignTicket_Click(object sender, RoutedEventArgs e)
        {
            if (TicketsGrid?.SelectedItem is not Ticket s) return;

            var role = _authService.CurrentUser?.Role;
            if (!Constants.UserRoles.IsEmployee(role))
            {
                MessageBox.Show("Доступ запрещён.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (Constants.UserRoles.IsAdmin(role))
                {
                    // Администратор назначает тикет любому оператору.
                    using var pickCtx = App.CreateDbContext();
                    var wnd = new AssignOperatorWindow(pickCtx, s) { Owner = this };
                    if (wnd.ShowDialog() == true && wnd.SelectedEmployeeId is int empId)
                    {
                        await _ticketService.AssignAsync(s.Id, empId);
                        await LoadTicketsAsync();
                    }
                }
                else
                {
                    // Оператор берёт тикет в работу на себя (с проверкой лимита нагрузки).
                    var eid = await GetCurrentEmployeeIdOrNullAsync();
                    if (eid != null)
                    {
                        await _ticketService.AssignAsync(s.Id, eid.Value);
                        await LoadTicketsAsync();
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                // Бизнес-ограничение (например, превышен лимит нагрузки оператора).
                MessageBox.Show(ex.Message, "Назначение невозможно", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка назначения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteTicket_Click(object sender, RoutedEventArgs e)
        {
            if (TicketsGrid?.SelectedItem is not Ticket s) return;

            // Удаление тикета — только администратор (двойная проверка).
            if (!Constants.UserRoles.IsAdmin(_authService.CurrentUser?.Role))
            {
                MessageBox.Show("Доступ запрещён.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Удалить тикет #{s.Id}?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await _ticketService.DeleteAsync(s.Id);

                using (var db = App.CreateDbContext())
                {
                    db.AuditLogs.Add(new AuditLog
                    {
                        Username = _authService.CurrentUser?.Username ?? "Система",
                        Action = "Удаление тикета",
                        Details = $"Удалён тикет #{s.Id}: {s.Title}",
                        Timestamp = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                }

                await LoadTicketsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Вспомогательные методы
        private void LoadSavedSearches()
        {
            var current = _authService.CurrentUser;
            if (current == null || SavedSearchComboBox == null) return;

            try
            {
                var presets = _context.SearchPresets.AsNoTracking().Where(p => p.UserId == current.Id).OrderBy(p => p.Name).ToList();
                SavedSearchComboBox.ItemsSource = presets;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки пресетов: {ex.Message}");
            }
        }

        private void SaveSearchButton_Click(object sender, RoutedEventArgs e)
        {
            var current = _authService.CurrentUser;
            if (current == null) return;

            string name = Interaction.InputBox("Введите название для фильтра:", "Сохранение поиска", "Мой фильтр");
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                var preset = new SearchPreset
                {
                    UserId = current.Id,
                    Name = name.Trim(),
                    TextQuery = SearchBox?.Text?.Trim(),
                    CreatedFrom = CreatedFromPicker.SelectedDate,
                    CreatedTo = CreatedToPicker.SelectedDate
                };

                if (StatusFilter.SelectedItem is ComboBoxItem si && si.Content is string st && st != "Все" && st != "Все статусы") preset.Status = st;
                if (CategoryFilter.SelectedItem is TicketCategory cat) preset.Category = cat;
                if (PriorityFilter.SelectedItem is TicketPriority prio) preset.Priority = prio;

                _context.SearchPresets.Add(preset);
                _context.SaveChanges();
                LoadSavedSearches();
                MessageBox.Show("Фильтр сохранен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
        }

        private void SavedSearchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SavedSearchComboBox.SelectedItem is not SearchPreset preset) return;

            if (SearchBox != null) SearchBox.Text = preset.TextQuery ?? string.Empty;
            if (CreatedFromPicker != null) CreatedFromPicker.SelectedDate = preset.CreatedFrom;
            if (CreatedToPicker != null) CreatedToPicker.SelectedDate = preset.CreatedTo;

            if (!string.IsNullOrEmpty(preset.Status) && StatusFilter != null)
            {
                foreach (ComboBoxItem item in StatusFilter.Items)
                {
                    if (item.Content?.ToString() == preset.Status)
                    {
                        StatusFilter.SelectedItem = item;
                        break;
                    }
                }
            }
            else if (StatusFilter != null) StatusFilter.SelectedIndex = 0;

            if (preset.Category.HasValue && CategoryFilter != null) CategoryFilter.SelectedItem = preset.Category.Value;
            if (preset.Priority.HasValue && PriorityFilter != null) PriorityFilter.SelectedItem = preset.Priority.Value;

            _ = LoadTicketsAsync();
        }

        private void DeleteSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SavedSearchComboBox.SelectedItem is not SearchPreset preset)
            {
                MessageBox.Show("Выберите фильтр.", "Внимание");
                return;
            }

            if (MessageBox.Show($"Удалить '{preset.Name}'?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            try
            {
                var entity = _context.SearchPresets.FirstOrDefault(p => p.Id == preset.Id);
                if (entity != null)
                {
                    _context.SearchPresets.Remove(entity);
                    _context.SaveChanges();
                    LoadSavedSearches();
                    SavedSearchComboBox.SelectedIndex = -1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tickets = TicketsGrid.ItemsSource as IEnumerable<Ticket>;
                if (tickets == null || !tickets.Any()) { MessageBox.Show("Нет данных."); return; }

                var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"Tickets_{DateTime.Now:yyyyMMdd}.csv" };
                if (dlg.ShowDialog() == true)
                {
                    var sb = new StringBuilder("ID;Title;Status;Category;Priority;Assignee;CreatedAt\n");
                    foreach (var t in tickets)
                        sb.AppendLine($"{t.Id};{t.Title};{t.Status};{t.Category};{t.Priority};{t.Assignee?.User?.Username};{t.CreatedAt}");

                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("Экспорт завершен.");
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tickets = TicketsGrid.ItemsSource as IEnumerable<Ticket>;
                if (tickets == null || !tickets.Any()) { MessageBox.Show("Нет данных."); return; }

                var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = $"Tickets_{DateTime.Now:yyyyMMdd}.pdf" };
                if (dlg.ShowDialog() == true)
                {
                    using var document = new PdfDocument();
                    document.Info.Title = "Список тикетов";
                    var page = document.AddPage();
                    var gfx = XGraphics.FromPdfPage(page);
                    var font = new XFont("Verdana", 10, XFontStyle.Regular);
                    double y = 40;

                    foreach (var t in tickets)
                    {
                        if (y > page.Height - 40) { page = document.AddPage(); gfx = XGraphics.FromPdfPage(page); y = 40; }
                        gfx.DrawString($"#{t.Id} {t.Title} [{t.Status}]", font, XBrushes.Black, 40, y);
                        y += 20;
                    }
                    document.Save(dlg.FileName);
                    MessageBox.Show("PDF создан.");
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void OpenNotifications_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser != null)
            {
                new NotificationCenterWindow(_authService.CurrentUser.Id) { Owner = this }.ShowDialog();
                UpdateNotificationsButtonCaption();
            }
        }

        private void OpenNotificationSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser != null)
                new NotificationSettingsWindow(_authService.CurrentUser.Id) { Owner = this }.ShowDialog();
        }

        private void OpenProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_authService.CurrentUser != null)
                new UserProfileWindow(_authService) { Owner = this }.ShowDialog();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            _authService.Logout();
            var loginContext = App.CreateDbContext();
            new LoginWindow(loginContext, new AuthService(loginContext)).Show();
            Close();
        }

        private void UpdateNotificationsButtonCaption()
        {
            if (OpenNotificationsButton == null || _authService.CurrentUser == null) return;
            try
            {
                var count = _context.Notifications.Count(n => n.UserId == _authService.CurrentUser.Id && !n.IsRead);
                OpenNotificationsButton.Content = count > 0 ? $"🔔 ({count})" : "🔔";
            }
            catch { }
        }

        private void ApplySavedTheme()
        {
            try
            {
                int? userId = _authService.CurrentUser?.Id;
                if (userId == null) return;

                using var db = App.CreateDbContext();
                var settings = db.UserUiSettings
                    .AsNoTracking()
                    .FirstOrDefault(s => s.UserId == userId.Value);

                bool isDark = settings?.Theme == "Dark";
                ThemeManager.ApplyTheme(isDark);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Theme] Не удалось применить тему: {ex.Message}");
            }
        }

        private void ConfigureAccess()
        {
            var role = _authService.CurrentUser?.Role;
            bool isAdmin = Constants.UserRoles.IsAdmin(role);
            bool isClient = Constants.UserRoles.IsClient(role);

            // Видимость пунктов навигации задаётся через RoleToVisibilityConverter в XAML.
            // Здесь — дополнительная защита для самых чувствительных пунктов (двойная проверка).
            // Дашборд и База знаний полностью управляются конвертером (Client их не видит /
            // видит соответственно), поэтому тут их трогать не нужно.
            if (EmployeesButton != null) EmployeesButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            if (AuditButton != null) AuditButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            if (CreateTicketButton != null) CreateTicketButton.Visibility = isClient ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task<int?> GetCurrentEmployeeIdOrNullAsync()
        {
            var u = _authService.CurrentUser;
            if (u == null) return null;
            var emp = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == u.Id);
            return emp?.Id;
        }
    }
}