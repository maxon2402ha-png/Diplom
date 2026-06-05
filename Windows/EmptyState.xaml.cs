using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class EmptyState : UserControl
    {
        public event EventHandler? ActionClick;

        public EmptyState()
        {
            InitializeComponent();
        }

        public string Icon
        {
            get => IconText.Text;
            set => IconText.Text = value;
        }

        public string Title
        {
            get => TitleText.Text;
            set => TitleText.Text = value;
        }

        public string Message
        {
            get => MessageText.Text;
            set => MessageText.Text = value;
        }

        public string? ActionText
        {
            get => ActionButton.Content as string;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    ActionButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ActionButton.Content = value;
                    ActionButton.Visibility = Visibility.Visible;
                }
            }
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            ActionClick?.Invoke(this, EventArgs.Empty);
        }
    }
}
