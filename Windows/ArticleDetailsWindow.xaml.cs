using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ArticleDetailsWindow : Window
    {
        private readonly int _articleId;
        private readonly int? _currentUserId;
        private bool _ratingSubmitted = false;

        // Session-scoped view dedup: (userId, articleId)
        private static readonly HashSet<(int, int)> _viewedThisSession = new();

        public ArticleDetailsWindow(KnowledgeArticle article, int? currentUserId = null)
        {
            InitializeComponent();
            DataContext = article;
            _articleId = article.Id;
            _currentUserId = currentUserId;

            Loaded += (_, _) => TrackView();
        }

        private void TrackView()
        {
            var key = (_currentUserId ?? 0, _articleId);
            if (!_viewedThisSession.Add(key)) return;

            try
            {
                using var db = App.CreateDbContext();
                var article = db.KnowledgeBase.Find(_articleId);
                if (article != null)
                {
                    article.ViewCount++;
                    db.SaveChanges();
                }
            }
            catch { }
        }

        private void Helpful_Click(object sender, RoutedEventArgs e) => SubmitRating(true);
        private void NotHelpful_Click(object sender, RoutedEventArgs e) => SubmitRating(false);

        private void SubmitRating(bool helpful)
        {
            if (_ratingSubmitted) return;
            _ratingSubmitted = true;

            HelpfulBtn.IsEnabled = false;
            NotHelpfulBtn.IsEnabled = false;

            try
            {
                using var db = App.CreateDbContext();
                var article = db.KnowledgeBase.Find(_articleId);
                if (article != null)
                {
                    if (helpful) article.HelpfulCount++;
                    else article.NotHelpfulCount++;
                    db.SaveChanges();
                }
            }
            catch { }

            RatingResultText.Text = helpful ? "Спасибо за оценку!" : "Мы учтём ваш отзыв.";
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
