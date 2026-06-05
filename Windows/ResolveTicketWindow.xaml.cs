using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ResolveTicketWindow : Window
    {
        public string ResolutionText { get; private set; } = string.Empty;
        public KnowledgeArticle? SelectedArticle { get; private set; }

        public ResolveTicketWindow(string? initialText = null)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(initialText))
                ResolutionBox.Text = initialText;

            LoadPublishedArticles();
            Loaded += (_, _) => ResolutionBox.Focus();
        }

        private void LoadPublishedArticles()
        {
            // Загрузка списка опубликованных статей не должна ронять окно.
            try
            {
                using var db = App.CreateDbContext();
                var articles = db.KnowledgeBase
                    .AsNoTracking()
                    .Where(a => a.IsPublished)
                    .OrderBy(a => a.Title)
                    .ToList();

                ArticleCombo.ItemsSource = articles;
            }
            catch
            {
                ArticleCombo.ItemsSource = new List<KnowledgeArticle>();
                ArticleCombo.IsEnabled = false;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var text = ResolutionBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                ErrorText.Visibility = Visibility.Visible;
                ResolutionBox.Focus();
                return;
            }

            ResolutionText = text;
            SelectedArticle = ArticleCombo.SelectedItem as KnowledgeArticle;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
