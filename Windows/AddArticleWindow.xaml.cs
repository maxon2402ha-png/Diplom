using System.Windows;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    public partial class AddArticleWindow : Window
    {
        private readonly AppDbContext _context;

        public AddArticleWindow(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
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
                ContentError.Text = "Заполните содержание статьи";
                ContentError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (invalid) return;

            _context.KnowledgeBase.Add(new KnowledgeArticle
            {
                Title = TitleBox.Text,
                Content = ContentBox.Text
            });

            _context.SaveChanges();

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