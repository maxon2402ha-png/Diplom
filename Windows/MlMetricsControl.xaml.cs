using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using КР_Ханников.Core;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class MlMetricsControl : UserControl
    {
        public MlMetricsControl()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadMetricsAsync();
        }

        private async Task LoadMetricsAsync()
        {
            try
            {
                using var db = App.CreateDbContext();
                var all = await db.MlModelMetrics
                    .AsNoTracking()
                    .OrderByDescending(m => m.TrainedAt)
                    .ToListAsync();

                if (all.Count == 0)
                {
                    HistoryGrid.Visibility = Visibility.Collapsed;
                    NoMetricsText.Visibility = Visibility.Visible;
                    ResetCards();
                    return;
                }

                HistoryGrid.Visibility = Visibility.Visible;
                NoMetricsText.Visibility = Visibility.Collapsed;

                HistoryGrid.ItemsSource = all.Select(m => new MetricRow(m)).ToList();

                var latestCat = all.FirstOrDefault(m => m.ModelType == "Category");
                FillCard(latestCat,
                    CatMicroText, CatMacroText, CatLossText, CatSampleText, CatTrainedText);

                var latestPrio = all.FirstOrDefault(m => m.ModelType == "Priority");
                FillCard(latestPrio,
                    PrioMicroText, PrioMacroText, PrioLossText, PrioSampleText, PrioTrainedText);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки метрик: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void FillCard(
            MlModelMetrics? m,
            TextBlock micro, TextBlock macro, TextBlock loss, TextBlock sample, TextBlock trained)
        {
            if (m == null)
            {
                micro.Text = macro.Text = loss.Text = sample.Text = "—";
                trained.Text = "Нет данных";
                return;
            }
            micro.Text = $"{m.MicroAccuracy * 100:F1}%";
            macro.Text = $"{m.MacroAccuracy * 100:F1}%";
            loss.Text = m.LogLoss.ToString("F3");
            sample.Text = m.SampleCount.ToString();
            trained.Text = $"Последнее обучение: {m.TrainedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
        }

        private void ResetCards()
        {
            FillCard(null, CatMicroText, CatMacroText, CatLossText, CatSampleText, CatTrainedText);
            FillCard(null, PrioMicroText, PrioMacroText, PrioLossText, PrioSampleText, PrioTrainedText);
        }

        private async void Retrain_Click(object sender, RoutedEventArgs e)
        {
            RetrainBtn.IsEnabled = false;
            RetrainBtn.Content = "Обучение...";

            try
            {
                await Task.Run(() =>
                {
                    using var db = App.CreateDbContext();
                    var classifier = new MlTicketClassifier();
                    classifier.TrainModels(db);
                });

                await LoadMetricsAsync();
                MessageBox.Show("Модель переобучена и метрики обновлены.", "Готово",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка переобучения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RetrainBtn.IsEnabled = true;
                RetrainBtn.Content = "🔄 Переобучить и оценить";
            }
        }

        public class MetricRow
        {
            private readonly MlModelMetrics _m;
            public MetricRow(MlModelMetrics m) { _m = m; }
            public string ModelType => _m.ModelType;
            public DateTime TrainedAt => _m.TrainedAt.ToLocalTime();
            public int SampleCount => _m.SampleCount;
            public string MicroAccuracyDisplay => $"{_m.MicroAccuracy * 100:F1}%";
            public string MacroAccuracyDisplay => $"{_m.MacroAccuracy * 100:F1}%";
            public string LogLossDisplay => _m.LogLoss.ToString("F3");
        }
    }
}
