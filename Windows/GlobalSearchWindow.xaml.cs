using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class GlobalSearchWindow : Window
    {
        private readonly AuthService _authService;
        private CancellationTokenSource? _cts;

        public GlobalSearchWindow(AuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            Loaded += (_, _) => SearchBox.Focus();
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var query = SearchBox.Text?.Trim() ?? "";
            if (query.Length < 2)
            {
                StatusText.Text = "Введите минимум 2 символа...";
                GroupsList.ItemsSource = null;
                return;
            }

            try
            {
                await Task.Delay(250, token);

                StatusText.Text = "Поиск...";

                using var db = App.CreateDbContext();
                var svc = new GlobalSearchService(db);
                var results = await svc.SearchAsync(query);

                if (token.IsCancellationRequested) return;

                if (results.TotalCount == 0)
                {
                    StatusText.Text = "Ничего не найдено";
                    GroupsList.ItemsSource = null;
                    return;
                }

                StatusText.Text = $"Найдено: {results.TotalCount}";
                var groups = new List<SearchGroup>();
                if (results.Tickets.Count > 0)  groups.Add(new SearchGroup($"🎫 Тикеты ({results.Tickets.Count})", results.Tickets));
                if (results.Articles.Count > 0) groups.Add(new SearchGroup($"📚 База знаний ({results.Articles.Count})", results.Articles));
                if (results.Comments.Count > 0) groups.Add(new SearchGroup($"💬 Комментарии ({results.Comments.Count})", results.Comments));
                if (results.Users.Count > 0)    groups.Add(new SearchGroup($"👥 Пользователи ({results.Users.Count})", results.Users));

                GroupsList.ItemsSource = groups;
            }
            catch (TaskCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
        }

        private void Hit_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not SearchHit hit) return;

            try
            {
                switch (hit.Kind)
                {
                    case SearchHitKind.Ticket:
                        OpenTicket(hit.Id);
                        break;
                    case SearchHitKind.Comment when hit.LinkedTicketId.HasValue:
                        OpenTicket(hit.LinkedTicketId.Value);
                        break;
                    case SearchHitKind.Article:
                        OpenArticle(hit.Id);
                        break;
                    case SearchHitKind.User:
                        MessageBox.Show($"Пользователь: {hit.Title} ({hit.Meta})", "Информация",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTicket(int ticketId)
        {
            using var ctx = App.CreateDbContext();
            var wnd = new TicketDetailsWindow(ticketId, ctx, _authService) { Owner = this };
            wnd.ShowDialog();
        }

        private void OpenArticle(int articleId)
        {
            using var db = App.CreateDbContext();
            var article = db.KnowledgeBase.AsNoTracking().FirstOrDefault(a => a.Id == articleId);
            if (article == null)
            {
                MessageBox.Show("Статья не найдена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var wnd = new ArticleDetailsWindow(article, _authService.CurrentUser?.Id) { Owner = this };
            wnd.ShowDialog();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public class SearchGroup
        {
            public string GroupTitle { get; }
            public List<SearchHit> Hits { get; }
            public SearchGroup(string title, List<SearchHit> hits)
            {
                GroupTitle = title;
                Hits = hits;
            }
        }
    }
}
