using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

using Constants = КР_Ханников.Core.Constants;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class TicketDetailsWindow : Window
    {
        private readonly int _ticketId;
        private readonly AppDbContext _context;
        private readonly AuthService _authService;
        private readonly NotificationService _notificationService;
        private readonly TicketService _ticketService;

        private Ticket _ticket = null!;
        private DispatcherTimer? _timer;
        private bool _isInitializing = true;

        public class CommentViewModel
        {
            public string AuthorName { get; set; } = string.Empty;
            public string? AvatarPath { get; set; }
            public string Text { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public bool IsMe { get; set; }
            public bool IsInternal { get; set; }
            public string Role { get; set; } = "Client";
        }

        public class AttachmentViewModel
        {
            public int Id { get; set; }
            public string FileName { get; set; } = string.Empty;
            public string StoredFilePath { get; set; } = string.Empty;
            public long FileSize { get; set; }
            public bool IsImage { get; set; }
            public bool CanDelete { get; set; }
            public int UploadedByUserId { get; set; }
            public string SizeLabel => AttachmentService.FormatSize(FileSize);
            public string Icon => IsImage ? "🖼" : GetFileIcon(FileName);
            public BitmapImage? ImageSource { get; set; }

            private static string GetFileIcon(string fileName)
            {
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                return ext switch
                {
                    ".pdf" => "📄",
                    ".docx" or ".doc" => "📝",
                    ".xlsx" or ".xls" => "📊",
                    ".zip" => "🗜",
                    ".txt" or ".log" => "📃",
                    _ => "📎"
                };
            }
        }

        public TicketDetailsWindow(int ticketId) : this(ticketId, new AppDbContext(), null!) { }
        public TicketDetailsWindow(int ticketId, AppDbContext context) : this(ticketId, context, null!) { }
        public TicketDetailsWindow(Ticket ticket, AppDbContext context, AuthService authService)
            : this(ticket.Id, context, authService) { }

        public TicketDetailsWindow(int ticketId, AppDbContext context, AuthService authService)
        {
            InitializeComponent();
            _ticketId = ticketId;
            _context = context ?? throw new ArgumentNullException(nameof(context));

            _authService = authService ?? new AuthService(_context);
            _notificationService = new NotificationService(_context, _authService);
            _ticketService = new TicketService(_context);

            SetupComboBoxes();
            Loaded += Window_Loaded;
        }

        private void SetupComboBoxes()
        {
                        StatusCombo.ItemsSource = Ticket.AllStatuses;
            PriorityCombo.ItemsSource = Enum.GetValues(typeof(TicketPriority));
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTicketDataAsync();
        }

        private async Task LoadTicketDataAsync()
        {
            _isInitializing = true;
            try
            {
                _context.ChangeTracker.Clear();

                var t = await _context.Tickets
                    .Include(x => x.Client)
                    .Include(x => x.Assignee).ThenInclude(a => a!.User)
                    .Include(x => x.Solution)
                    .Include(x => x.Feedback)
                    .Include(x => x.Comments).ThenInclude(c => c.Author)
                    .Include(x => x.History)
                    .Include(x => x.Attachments)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == _ticketId);

                if (t == null)
                {
                    MessageBox.Show("Тикет не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                // Защита владения: клиент может открывать только свои обращения.
                if (_authService.CurrentUser is { } cu && Constants.UserRoles.IsClient(cu.Role))
                {
                    var ownClientId = await _context.Clients
                        .AsNoTracking()
                        .Where(c => c.UserId == cu.Id)
                        .Select(c => (int?)c.Id)
                        .FirstOrDefaultAsync();

                    if (ownClientId == null || t.ClientId != ownClientId.Value)
                    {
                        MessageBox.Show("Доступ запрещён: вы можете просматривать только свои обращения.",
                            "Доступ запрещён", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Close();
                        return;
                    }
                }

                _ticket = t;

                                TicketIdHeader.Text = $"#{_ticket.Id}";
                TicketTitleHeader.Text = _ticket.Title;
                DescriptionText.Text = string.IsNullOrWhiteSpace(_ticket.Description) ? "Нет описания" : _ticket.Description;
                StatusText.Text = _ticket.Status;
                StatusBadge.Background = GetStatusBrush(_ticket.Status);

                ClientNameText.Text = _ticket.Client?.Name ?? "Неизвестно";
                ClientEmailText.Text = _ticket.Client?.Email ?? "Нет email";

                var assigneeName = "Не назначен";
                var currentAssignee = _ticket.Assignee;
                if (currentAssignee != null)
                {
                    assigneeName = currentAssignee.User?.Username ?? "Сотрудник";
                }

                AssigneeText.Text = assigneeName;
                CategoryText.Text = _ticket.Category.ToString();
                PriorityText.Text = _ticket.Priority.ToString();

                StatusCombo.SelectedItem = _ticket.Status;
                PriorityCombo.SelectedItem = _ticket.Priority;

                DataContext = _ticket;

                UpdateSlaVisuals();
                SetupWorkTimer();
                CheckAttachment();
                CheckSolution();
                ConfigureAccessAndState();
                LoadComments();
                LoadHistory();
                _ = LoadRecommendationsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки тикета: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void ConfigureAccessAndState()
        {
            var user = _authService.CurrentUser;
            if (user == null) return;

            bool isClient = user.Role == Constants.UserRoles.Client;
            bool isClosed = _ticket.Status == Constants.TicketStatus.Closed;
            bool isResolved = _ticket.Status == Constants.TicketStatus.Resolved;
            bool isSupport = user.Role == Constants.UserRoles.Support || user.Role == Constants.UserRoles.Admin;

            OperatorControlsPanel.Visibility = (isSupport && !isClosed && !isResolved) ? Visibility.Visible : Visibility.Collapsed;
            InternalCheck.Visibility = isSupport ? Visibility.Visible : Visibility.Collapsed;
            InternalCheck.IsChecked = false;

            if (isClient)
            {
                CloseTicketButton.Visibility = Visibility.Collapsed;
                EditButton.Visibility = Visibility.Collapsed;

                if (isResolved && FindName("ClientConfirmPanel") is System.Windows.Controls.Border confirmPanel)
                {
                    confirmPanel.Visibility = Visibility.Visible;
                    RateButton.Visibility = Visibility.Collapsed;
                }
                else if (isClosed)
                {
                    if (FindName("ClientConfirmPanel") is System.Windows.Controls.Border cp2)
                        cp2.Visibility = Visibility.Collapsed;

                    RateButton.Visibility = Visibility.Visible;
                    if (_ticket.Feedback != null)
                    {
                        RateButton.Content = $"Оценка: {_ticket.Feedback.Rating}/5";
                        RateButton.IsEnabled = false;
                    }
                    else
                    {
                        RateButton.Content = "⭐ Оценить работу";
                        RateButton.IsEnabled = true;
                    }
                }
                else
                {
                    RateButton.Visibility = Visibility.Collapsed;
                    if (FindName("ClientConfirmPanel") is System.Windows.Controls.Border cp3)
                        cp3.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                RateButton.Visibility = Visibility.Collapsed;
                if (FindName("ClientConfirmPanel") is System.Windows.Controls.Border cp4)
                    cp4.Visibility = Visibility.Collapsed;
                CloseTicketButton.Visibility = (isClosed || isResolved) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void UpdateSlaVisuals()
        {
                        if (FindName("SlaStatusText") is not TextBlock slaStatus ||
                FindName("SlaProgressBar") is not ProgressBar slaProgress)
                return;

            if (_ticket.Status == Constants.TicketStatus.Closed || _ticket.Status == Constants.TicketStatus.Resolved)
            {
                slaStatus.Text = "Заявка решена";
                slaStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                slaProgress.Value = 100;
                slaProgress.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                return;
            }

            if (!_ticket.DueAt.HasValue)
            {
                slaStatus.Text = "SLA не установлен";
                slaStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                slaProgress.Value = 0;
                return;
            }

            var totalTime = _ticket.DueAt.Value - _ticket.CreatedAt;
            var timeRemaining = _ticket.DueAt.Value - DateTime.UtcNow;
            var timePassed = DateTime.UtcNow - _ticket.CreatedAt;

            if (timeRemaining.TotalSeconds <= 0)
            {
                slaStatus.Text = "ПРОСРОЧЕНО";
                slaStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                slaProgress.Value = 100;
                slaProgress.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            }
            else
            {
                string remainingText = timeRemaining.TotalHours >= 24
                    ? $"{(int)timeRemaining.TotalDays} дн. {timeRemaining.Hours} ч."
                    : $"{(int)timeRemaining.TotalHours} ч. {timeRemaining.Minutes} мин.";

                slaStatus.Text = remainingText;
                slaStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));

                double percent = (timePassed.TotalSeconds / totalTime.TotalSeconds) * 100;
                slaProgress.Value = Math.Min(percent, 100);

                if (percent > 80) slaProgress.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                else slaProgress.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
            }
        }

        private void LoadComments()
        {
            try
            {
                var currentUserId = _authService.CurrentUser?.Id ?? 0;
                var commentsQuery = _ticket.Comments.AsEnumerable();

                if (_authService.CurrentUser?.Role == Constants.UserRoles.Client)
                {
                    commentsQuery = commentsQuery.Where(c => !c.IsInternal);
                }

                var rawComments = commentsQuery.OrderBy(c => c.CreatedAt).ToList();
                var authorIds = rawComments.Select(c => c.UserId).Distinct().ToList();

                var clients = _context.Clients.Where(c => authorIds.Contains(c.UserId)).ToDictionary(c => c.UserId, c => c.Name);
                var employees = _context.Employees.Include(e => e.User).Where(e => authorIds.Contains(e.UserId)).ToList();
                var employeeNames = new Dictionary<int, string>();
                foreach (var emp in employees) employeeNames[emp.UserId] = emp.User?.Username ?? "Сотрудник";

                var viewModels = rawComments.Select(c =>
                {
                    string displayName = c.Author?.Username ?? "Система";

                                        if (clients.TryGetValue(c.UserId, out var clientName))
                        displayName = clientName;
                    else if (employeeNames.TryGetValue(c.UserId, out var empName))
                        displayName = empName;

                    return new CommentViewModel
                    {
                        AuthorName = displayName,
                        AvatarPath = c.Author?.AvatarPath,
                        Text = c.Text,
                        CreatedAt = c.CreatedAt.ToLocalTime(),
                        IsMe = c.UserId == currentUserId,
                        IsInternal = c.IsInternal,
                        Role = c.Author?.Role ?? "Client"
                    };
                }).ToList();

                                CommentsList.ItemsSource = viewModels;
                CommentsCountText.Text = viewModels.Count.ToString();

                Dispatcher.InvokeAsync(() => CommentsScrollViewer?.ScrollToBottom(), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading comments: {ex.Message}");
            }
        }

        private void LoadHistory()
        {
            HistoryGrid.ItemsSource = _ticket.History.OrderByDescending(h => h.Timestamp).ToList();
        }

        private async Task LoadRecommendationsAsync()
        {
            // Показываем только клиенту, и только пока нет решения
            var user = _authService.CurrentUser;
            if (user == null || user.Role != Constants.UserRoles.Client) return;
            if (_ticket.Solution != null) return;

            try
            {
                RecommendationsPanel.Visibility = Visibility.Visible;
                RecommendationsLoadingText.Visibility = Visibility.Visible;
                RecommendationsList.ItemsSource = null;

                List<RecommendedArticle> recommendations;
                using (var db = App.CreateDbContext())
                {
                    var svc = new ArticleRecommendationService(db);
                    recommendations = await svc.RecommendAsync(_ticket.Title, _ticket.Description ?? "", 5);
                }

                RecommendationsLoadingText.Visibility = Visibility.Collapsed;

                if (recommendations.Count == 0)
                {
                    RecommendationsPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    RecommendationsList.ItemsSource = recommendations;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Recommendations] {ex.Message}");
                RecommendationsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void RecommendedArticle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is KnowledgeArticle article)
            {
                try
                {
                    var wnd = new ArticleDetailsWindow(article, _authService.CurrentUser?.Id) { Owner = this };
                    wnd.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть статью: {ex.Message}", "Ошибка");
                }
            }
        }

        private async void SendComment_Click(object sender, RoutedEventArgs e)
        {
            var text = CommentBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_authService.CurrentUser == null) return;

            try
            {
                var user = _authService.CurrentUser;
                bool isInternal = false;

                                if (user.Role != Constants.UserRoles.Client)
                {
                    isInternal = InternalCheck.IsChecked == true;
                }

                using var isolatedDb = App.CreateDbContext();
                var isolatedTicketService = new TicketService(isolatedDb);

                await isolatedTicketService.AddCommentAsync(_ticketId, user.Id, text, isInternal);

                if (user.Role == Constants.UserRoles.Client && _ticket.AssigneeEmployeeId.HasValue)
                {
                    var emp = await isolatedDb.Employees.FindAsync(_ticket.AssigneeEmployeeId.Value);
                    if (emp != null) _notificationService.NotifyOperatorsAboutNewTicket(_ticket);
                }

                CommentBox.Clear();
                InternalCheck.IsChecked = false;

                await LoadTicketDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || StatusCombo.SelectedItem == null) return;

            var newStatus = StatusCombo.SelectedItem.ToString();

            try
            {
                using var db = App.CreateDbContext();
                var ticketService = new TicketService(db);
                await ticketService.ChangeStatusAsync(_ticketId, newStatus!);
                await LoadTicketDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при смене статуса: {ex.Message}");
            }
        }

        private async void PriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || PriorityCombo.SelectedItem == null) return;

            var newPriority = (TicketPriority)PriorityCombo.SelectedItem;

            try
            {
                using var db = App.CreateDbContext();
                var ticket = await db.Tickets.FindAsync(_ticketId);

                if (ticket != null && ticket.Priority != newPriority)
                {
                    ticket.Priority = newPriority;

                    db.TicketHistories.Add(new TicketHistory
                    {
                        TicketId = _ticketId,
                        Action = "Изменение приоритета",
                        Details = $"Приоритет изменен на {newPriority}",
                        Timestamp = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync();
                    await LoadTicketDataAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при смене приоритета: {ex.Message}");
            }
        }

        private void SetupWorkTimer()
        {
            _timer?.Stop();

            if (_ticket.Status != Constants.TicketStatus.InProgress && _ticket.Status != Constants.TicketStatus.Open)
            {
                TimerText.Text = "Остановлен";
                TimerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                TimerBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
                return;
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                var elapsed = DateTime.UtcNow - _ticket.CreatedAt.ToUniversalTime();
                TimerText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            };

            TimerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
            TimerBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
            _timer.Start();
        }

        private async void CloseTicket_Click(object sender, RoutedEventArgs e)
        {
            var resolveWnd = new ResolveTicketWindow("Вопрос решён штатным образом.") { Owner = this };
            if (resolveWnd.ShowDialog() != true)
                return;

            string resolutionText = resolveWnd.ResolutionText;
            if (string.IsNullOrWhiteSpace(resolutionText))
                return;

            // Необязательная привязка статьи базы знаний к решению.
            if (resolveWnd.SelectedArticle != null)
                resolutionText += $"\n\nСтатья базы знаний: {resolveWnd.SelectedArticle.Title} (#{resolveWnd.SelectedArticle.Id})";

            CloseTicketButton.IsEnabled = false;
            try
            {
                using var db = App.CreateDbContext();
                var t = await db.Tickets.FindAsync(_ticketId);
                if (t != null)
                {
                    var oldStatus = t.Status;
                    t.Status = Constants.TicketStatus.Resolved;
                    t.ClientConfirmationDeadline = DateTime.UtcNow.AddHours(72);

                    db.Solutions.Add(new Solution
                    {
                        TicketId = _ticketId,
                        ResolutionText = resolutionText,
                        ResolutionDate = DateTime.UtcNow
                    });

                    db.TicketHistories.Add(new TicketHistory
                    {
                        TicketId = _ticketId,
                        Action = "Отмечен решённым",
                        Details = $"Статус: {oldStatus} → Resolved. Ждём подтверждения клиента (72 ч).",
                        Timestamp = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync();
                    _notificationService.NotifyStatusChanged(t, oldStatus, Constants.TicketStatus.Resolved);

                    await LoadTicketDataAsync();
                    MessageBox.Show("Тикет помечен как решённый.\nКлиент может подтвердить в течение 72 часов.",
                        "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при закрытии: {ex.Message}");
            }
            finally
            {
                CloseTicketButton.IsEnabled = true;
            }
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            var editWin = new EditTicketWindow(_ticketId, _context) { Owner = this };
            if (editWin.ShowDialog() == true)
            {
                await LoadTicketDataAsync();
            }
        }

        private async void Rate_Click(object sender, RoutedEventArgs e)
        {
            var ratingWnd = new FeedbackRatingWindow { Owner = this };
            if (ratingWnd.ShowDialog() != true || ratingWnd.Rating == 0) return;

            try
            {
                using var db = App.CreateDbContext();
                var feedback = new Feedback
                {
                    TicketId = _ticketId,
                    ClientId = _ticket.ClientId,
                    SupportId = _ticket.AssigneeEmployeeId,
                    Rating = ratingWnd.Rating,
                    Comment = ratingWnd.Comment,
                    CreatedAt = DateTime.UtcNow
                };
                db.Feedbacks.Add(feedback);
                await db.SaveChangesAsync();

                MessageBox.Show("Спасибо за ваш отзыв!", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadTicketDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
        }

        private async void ConfirmResolution_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var db = App.CreateDbContext();
                var t = await db.Tickets.FindAsync(_ticketId);
                if (t == null) return;

                var oldStatus = t.Status;
                t.Status = Constants.TicketStatus.Closed;
                t.ClosedAt = DateTime.UtcNow;
                t.ClientConfirmationDeadline = null;

                db.TicketHistories.Add(new TicketHistory
                {
                    TicketId = _ticketId,
                    Action = "Подтверждение клиентом",
                    Details = "Клиент подтвердил решение тикета.",
                    Timestamp = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
                _notificationService.NotifyStatusChanged(t, oldStatus, Constants.TicketStatus.Closed);
                await LoadTicketDataAsync();

                var ratingWnd = new FeedbackRatingWindow { Owner = this };
                if (ratingWnd.ShowDialog() == true && ratingWnd.Rating > 0)
                {
                    using var db2 = App.CreateDbContext();
                    db2.Feedbacks.Add(new Feedback
                    {
                        TicketId = _ticketId,
                        ClientId = _ticket.ClientId,
                        SupportId = _ticket.AssigneeEmployeeId,
                        Rating = ratingWnd.Rating,
                        Comment = ratingWnd.Comment,
                        CreatedAt = DateTime.UtcNow
                    });
                    await db2.SaveChangesAsync();
                    await LoadTicketDataAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private async void RejectResolution_Click(object sender, RoutedEventArgs e)
        {
            var commentWnd = new AddCommentWindow { Owner = this };
            commentWnd.Title = "Укажите причину отклонения";
            if (commentWnd.ShowDialog() != true) return;

            var rejectReason = commentWnd.CommentText;
            if (string.IsNullOrWhiteSpace(rejectReason))
            {
                MessageBox.Show("Необходимо указать причину.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var db = App.CreateDbContext();
                var t = await db.Tickets.FindAsync(_ticketId);
                if (t == null) return;

                var oldStatus = t.Status;
                t.Status = Constants.TicketStatus.InProgress;
                t.ClientConfirmationDeadline = null;

                db.TicketHistories.Add(new TicketHistory
                {
                    TicketId = _ticketId,
                    Action = "Отклонение клиентом",
                    Details = $"Клиент отклонил решение. Причина: {rejectReason}",
                    Timestamp = DateTime.UtcNow
                });

                db.TicketComments.Add(new TicketComment
                {
                    TicketId = _ticketId,
                    UserId = _authService.CurrentUser!.Id,
                    Text = $"[Отклонение] {rejectReason}",
                    IsInternal = false,
                    CreatedAt = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
                _notificationService.NotifyStatusChanged(t, oldStatus, Constants.TicketStatus.InProgress);
                await LoadTicketDataAsync();

                MessageBox.Show("Тикет возвращён в работу.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private void CheckAttachment()
        {
            LoadAttachmentsList();
        }

        private void LoadAttachmentsList()
        {
            var currentUser = _authService.CurrentUser;
            bool isAdmin = currentUser?.Role == Constants.UserRoles.Admin;

            var viewModels = _ticket.Attachments
                .OrderBy(a => a.UploadedAt)
                .Select(a =>
                {
                    BitmapImage? img = null;
                    if (AttachmentService.IsImage(a.FileName) && File.Exists(a.StoredFilePath))
                    {
                        try
                        {
                            img = new BitmapImage();
                            img.BeginInit();
                            img.UriSource = new Uri(a.StoredFilePath);
                            img.DecodePixelWidth = 80;
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.EndInit();
                            img.Freeze();
                        }
                        catch { img = null; }
                    }

                    return new AttachmentViewModel
                    {
                        Id = a.Id,
                        FileName = a.FileName,
                        StoredFilePath = a.StoredFilePath,
                        FileSize = a.FileSize,
                        IsImage = AttachmentService.IsImage(a.FileName) && img != null,
                        CanDelete = isAdmin || a.UploadedByUserId == currentUser?.Id,
                        UploadedByUserId = a.UploadedByUserId,
                        ImageSource = img
                    };
                })
                .ToList();

            AttachmentsList.ItemsSource = viewModels;
            NoAttachmentsText.Visibility = viewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            bool isClosed = _ticket.Status == Constants.TicketStatus.Closed;
            bool isClient = currentUser?.Role == Constants.UserRoles.Client;
            AddAttachmentBtn.Visibility = isClosed ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CheckSolution()
        {
            bool hasSol = _ticket.Solution != null;
            SolutionPanel.Visibility = hasSol ? Visibility.Visible : Visibility.Collapsed;
            if (hasSol)
            {
                SolutionText.Text = _ticket.Solution!.ResolutionText;
            }
        }

        private void OpenAttachmentItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is AttachmentViewModel vm)
            {
                try
                {
                    if (!File.Exists(vm.StoredFilePath))
                    {
                        MessageBox.Show("Файл не найден на диске.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Process.Start(new ProcessStartInfo { FileName = vm.StoredFilePath, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть файл: {ex.Message}", "Ошибка");
                }
            }
        }

        private async void DeleteAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not AttachmentViewModel vm) return;

            var confirm = MessageBox.Show($"Удалить вложение «{vm.FileName}»?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using var db = App.CreateDbContext();
                var attachment = await db.TicketAttachments.FindAsync(vm.Id);
                if (attachment != null)
                    await AttachmentService.DeleteAsync(attachment, db);

                await LoadTicketDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка");
            }
        }

        private async void AddAttachment_Click(object sender, RoutedEventArgs e)
        {
            var currentUser = _authService.CurrentUser;
            if (currentUser == null) return;

            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Поддерживаемые файлы|*.png;*.jpg;*.jpeg;*.pdf;*.log;*.txt;*.docx;*.xlsx;*.zip",
                Title = "Выберите файлы"
            };

            if (dlg.ShowDialog() != true) return;

            var errors = new List<string>();
            using var db = App.CreateDbContext();

            foreach (var filePath in dlg.FileNames)
            {
                var (ok, error) = AttachmentService.ValidateFile(filePath);
                if (!ok)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: {error}");
                    continue;
                }

                try
                {
                    await AttachmentService.SaveAsync(filePath, _ticketId, currentUser.Id, db);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
                MessageBox.Show(string.Join("\n", errors), "Некоторые файлы не приняты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

            await LoadTicketDataAsync();
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

                private static SolidColorBrush GetStatusBrush(string status)
        {
            return status switch
            {
                Constants.TicketStatus.Open => new SolidColorBrush(Colors.Orange),
                Constants.TicketStatus.InProgress => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                Constants.TicketStatus.Resolved => new SolidColorBrush(Color.FromRgb(39, 174, 96)),
                Constants.TicketStatus.Closed => new SolidColorBrush(Colors.Gray),
                _ => new SolidColorBrush(Colors.Black)
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}