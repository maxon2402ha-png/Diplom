using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class LoadingOverlay : UserControl
    {
        private readonly Storyboard _spinAnimation;

        public LoadingOverlay()
        {
            InitializeComponent();
            _spinAnimation = (Storyboard)FindResource("SpinAnimation");
        }

        public void Show(string? title = null, string? message = null)
        {
            if (!string.IsNullOrEmpty(title)) TitleText.Text = title;

            if (!string.IsNullOrEmpty(message))
            {
                MessageText.Text = message;
                MessageText.Visibility = Visibility.Visible;
            }
            else
            {
                MessageText.Visibility = Visibility.Collapsed;
            }

            Visibility = Visibility.Visible;
            // Запускаем вращение спиннера. this — область имён UserControl,
            // в которой зарегистрирован SpinnerTransform (TargetName анимации).
            _spinAnimation.Begin(this, isControllable: true);
        }

        public void Hide()
        {
            _spinAnimation.Stop(this);
            Visibility = Visibility.Collapsed;
        }
    }
}
