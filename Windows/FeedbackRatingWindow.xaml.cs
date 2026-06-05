using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class FeedbackRatingWindow : Window
    {
        private int _rating;
        private readonly Button[] _stars = null!;

        public int Rating => _rating;
        public string Comment => CommentBox.Text.Trim();

        public FeedbackRatingWindow()
        {
            InitializeComponent();
            _stars = new[] { Star1, Star2, Star3, Star4, Star5 };
        }

        private void Star_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int tag))
            {
                _rating = tag;
                UpdateStars();
            }
        }

        private void UpdateStars()
        {
            var active = new SolidColorBrush(Color.FromRgb(234, 179, 8));
            var inactive = new SolidColorBrush(Color.FromRgb(209, 213, 219));
            for (int i = 0; i < _stars.Length; i++)
                _stars[i].Foreground = i < _rating ? active : inactive;
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (_rating == 0)
            {
                MessageBox.Show("Выберите оценку от 1 до 5 звёзд.", "Оценка обязательна",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            _rating = 0;
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
