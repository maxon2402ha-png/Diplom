using System.Windows;
using System.Windows.Media;

namespace КР_Ханников.Helpers
{
    public static class ThemeManager
    {
        public static bool IsDarkTheme { get; private set; } = false;

        public static void ApplyTheme(bool isDark)
        {
            IsDarkTheme = isDark;
            var dict = Application.Current.Resources;

            if (isDark)
            {
                SetBrush(dict, "Brush.Background", "#0C0C0E");
                SetBrush(dict, "Brush.Surface", "#141417");
                SetBrush(dict, "Brush.SurfaceAlt", "#1C1C20");
                SetBrush(dict, "Brush.Border", "#27272A");
                SetBrush(dict, "Brush.BorderStrong", "#3F3F46");
                SetBrush(dict, "Brush.Divider", "#27272A");

                SetBrush(dict, "Brush.TextPrimary", "#FAFAFA");
                SetBrush(dict, "Brush.TextSecondary", "#A1A1AA");
                SetBrush(dict, "Brush.TextMuted", "#71717A");
                SetBrush(dict, "Brush.TextTertiary", "#71717A");

                SetBrush(dict, "Brush.Primary", "#FAFAFA");
                SetBrush(dict, "Brush.PrimaryDark", "#E4E4E7");
                SetBrush(dict, "Brush.PrimaryLight", "#27272A");
                SetBrush(dict, "Brush.Accent", "#FAFAFA");
                SetBrush(dict, "Brush.TextOnPrimary", "#18181B");

                SetBrush(dict, "Brush.InfoLight", "#27272A");
                SetBrush(dict, "Brush.AccentLight", "#27272A");
            }
            else
            {
                SetBrush(dict, "Brush.Background", "#F6F6F7");
                SetBrush(dict, "Brush.Surface", "#FFFFFF");
                SetBrush(dict, "Brush.SurfaceAlt", "#F4F4F5");
                SetBrush(dict, "Brush.Border", "#E7E7EA");
                SetBrush(dict, "Brush.BorderStrong", "#D4D4D8");
                SetBrush(dict, "Brush.Divider", "#E7E7EA");

                SetBrush(dict, "Brush.TextPrimary", "#18181B");
                SetBrush(dict, "Brush.TextSecondary", "#52525B");
                SetBrush(dict, "Brush.TextMuted", "#A1A1AA");
                SetBrush(dict, "Brush.TextTertiary", "#A1A1AA");

                SetBrush(dict, "Brush.Primary", "#18181B");
                SetBrush(dict, "Brush.PrimaryDark", "#000000");
                SetBrush(dict, "Brush.PrimaryLight", "#E4E4E7");
                SetBrush(dict, "Brush.Accent", "#18181B");
                SetBrush(dict, "Brush.TextOnPrimary", "#FFFFFF");

                SetBrush(dict, "Brush.InfoLight", "#ECECEF");
                SetBrush(dict, "Brush.AccentLight", "#E4E4E7");
            }

            dict["Brush.TextMain"] = dict["Brush.TextPrimary"];
            dict["Brush.TextLight"] = dict["Brush.TextSecondary"];
        }

        private static void SetBrush(ResourceDictionary dict, string key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            dict[key] = new SolidColorBrush(color);
        }
    }
}