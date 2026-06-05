using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ErrorDetailsWindow : Window
    {
        private readonly string _details;

        public ErrorDetailsWindow(string summary, string details, string? logPath = null)
        {
            InitializeComponent();
            _details = details;

            SummaryText.Text = summary;
            DetailsBox.Text = details;

            if (!string.IsNullOrEmpty(logPath))
                LogPathText.Text = $"Лог: {logPath}";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_details);
                CopyButton.Content = "Скопировано ✓";
            }
            catch { /* буфер обмена может быть недоступен */ }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
