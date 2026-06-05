using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class KnowledgeBaseControl : UserControl
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;
        private List<KnowledgeArticle> _allArticles = new();
        private DispatcherTimer _searchTimer;

        public KnowledgeBaseControl(AppDbContext context, AuthService authService)
        {
            InitializeComponent();
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            DataContext = this;

                        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += SearchTimer_Tick;

            ConfigureAccess();
            _ = LoadArticlesAsync();
        }

        public User? CurrentUser => _authService.CurrentUser;

        private void ConfigureAccess()
        {
                        if (_authService.CurrentUser?.Role == Constants.UserRoles.Client)
            {
                if (AddArticleButton != null) AddArticleButton.Visibility = Visibility.Collapsed;
                if (ReaderActionButtons != null) ReaderActionButtons.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadArticlesAsync()
        {
            try
            {
                                using var db = App.CreateDbContext();

                _allArticles = await db.KnowledgeBase
                    .AsNoTracking()
                    .OrderByDescending(a => a.UpdatedAt)
                    .ToListAsync();

                UpdateGridSource(_allArticles);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки статей: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateGridSource(IEnumerable<KnowledgeArticle> articles)
        {
            if (ArticlesList == null) return;
            var list = articles.ToList();

            ArticlesList.ItemsSource = list;
            if (ArticleCountText != null) ArticleCountText.Text = list.Count.ToString();

                        if (ArticlesList.SelectedItem == null)
            {
                ShowEmptyState();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
                        _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();
            ApplyFilterAndSort();
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_allArticles.Count == 0) return;
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            var query = SearchBox?.Text?.Trim().ToLower() ?? "";

            IEnumerable<KnowledgeArticle> result = _allArticles;

            if (!string.IsNullOrWhiteSpace(query))
            {
                result = result.Where(a =>
                    (a.Title != null && a.Title.ToLower().Contains(query)) ||
                    (a.Content != null && a.Content.ToLower().Contains(query)));
            }

            var sortIndex = SortCombo?.SelectedIndex ?? 0;
            result = sortIndex switch
            {
                1 => result.OrderByDescending(a => a.ViewCount).ThenByDescending(a => a.UpdatedAt),
                2 => result.OrderByDescending(a => a.HelpfulCount - a.NotHelpfulCount).ThenByDescending(a => a.UpdatedAt),
                _ => result.OrderByDescending(a => a.UpdatedAt)
            };

            UpdateGridSource(result);
        }

                private void ArticlesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArticlesList.SelectedItem is KnowledgeArticle article)
            {
                                ReaderTitle.Text = article.Title;
                ReaderDate.Text = $"Последнее обновление: {article.UpdatedAt:dd.MM.yyyy HH:mm}";
                ReaderContent.Text = article.Content;

                                ReaderPanel.Visibility = Visibility.Visible;
                NoSelectionPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowEmptyState();
            }
        }

        private void ShowEmptyState()
        {
            if (ReaderPanel != null) ReaderPanel.Visibility = Visibility.Collapsed;
            if (NoSelectionPanel != null) NoSelectionPanel.Visibility = Visibility.Visible;
        }

                private async void AddArticle_Click(object sender, RoutedEventArgs e)
        {
            var editor = new ArticleEditorWindow { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                try
                {
                    using var db = App.CreateDbContext();
                    var article = new KnowledgeArticle
                    {
                        Title = editor.ArticleTitle,
                        Content = editor.ArticleContent,
                        AuthorId = _authService.CurrentUser!.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    db.KnowledgeBase.Add(article);
                    await db.SaveChangesAsync();

                                        await LoadArticlesAsync();
                    ArticlesList.SelectedItem = _allArticles.FirstOrDefault(a => a.Title == article.Title);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}");
                }
            }
        }

        private async void EditArticle_Click(object sender, RoutedEventArgs e)
        {
                        if (ArticlesList.SelectedItem is not KnowledgeArticle article) return;

            var editor = new ArticleEditorWindow(article) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                try
                {
                    using var db = App.CreateDbContext();
                    var item = await db.KnowledgeBase.FirstOrDefaultAsync(k => k.Id == article.Id);

                    if (item != null)
                    {
                        item.Title = editor.ArticleTitle;
                        item.Content = editor.ArticleContent;
                        item.UpdatedAt = DateTime.UtcNow;

                        await db.SaveChangesAsync();
                        await LoadArticlesAsync();

                                                ArticlesList.SelectedItem = _allArticles.FirstOrDefault(a => a.Id == article.Id);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка обновления: {ex.Message}");
                }
            }
        }

        private async void DeleteArticle_Click(object sender, RoutedEventArgs e)
        {
            if (ArticlesList.SelectedItem is not KnowledgeArticle article) return;

            if (MessageBox.Show($"Вы уверены, что хотите удалить статью \"{article.Title}\"?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    using var db = App.CreateDbContext();
                    var item = await db.KnowledgeBase.FirstOrDefaultAsync(k => k.Id == article.Id);

                    if (item != null)
                    {
                        db.KnowledgeBase.Remove(item);

        
                        db.AuditLogs.Add(new AuditLog
                        {
                            Username = _authService.CurrentUser?.Username ?? "Система",
                            Action = "Удаление статьи",
                            Details = $"Удалена статья: {item.Title}",
                            Timestamp = DateTime.UtcNow
                        });

                        await db.SaveChangesAsync();
                        await LoadArticlesAsync();
                        ShowEmptyState();                     }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}");
                }
            }
        }
    }
}