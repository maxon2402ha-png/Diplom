using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ArticleEditorWindow : Window
    {
        private readonly KnowledgeArticle? _article;

        public string ArticleTitle { get; private set; } = string.Empty;
        public string ArticleContent { get; private set; } = string.Empty;

                public ArticleEditorWindow()
        {
            InitializeComponent();
            WindowTitle.Text = "Новая статья";
            TitleBox.Focus();
        }

                public ArticleEditorWindow(KnowledgeArticle article)
        {
            InitializeComponent();
            _article = article;
            WindowTitle.Text = "Редактирование статьи";

            TitleBox.Text = article.Title;
            ContentBox.Text = article.Content;
        }

                private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TitleError.Visibility = Visibility.Collapsed;
            ContentError.Visibility = Visibility.Collapsed;

            bool invalid = false;
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleError.Text = "Укажите заголовок";
                TitleError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (string.IsNullOrWhiteSpace(ContentBox.Text))
            {
                ContentError.Text = "Заполните содержание";
                ContentError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (invalid) return;

            ArticleTitle = TitleBox.Text.Trim();
            ArticleContent = ContentBox.Text.Trim();

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