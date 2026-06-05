using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.IO;
using System.Linq;
using КР_Ханников.Core;
using КР_Ханников.Data;
using System.Collections.Generic;

namespace КР_Ханников.Services
{
        public class TicketInput
    {
        [LoadColumn(0)] public string Title { get; set; } = string.Empty;
        [LoadColumn(1)] public string Description { get; set; } = string.Empty;
        [LoadColumn(2)] public string Category { get; set; } = string.Empty;
        [LoadColumn(3)] public string Priority { get; set; } = string.Empty;
    }

        public class CategoryPrediction
    {
        [ColumnName("PredictedLabel")] public string PredictedCategory { get; set; } = string.Empty;
    }

    public class PriorityPrediction
    {
        [ColumnName("PredictedLabel")] public string PredictedPriority { get; set; } = string.Empty;
    }

        public class MlTicketClassifier
    {
        private readonly MLContext _mlContext;
        private readonly string _categoryModelPath = "TicketCategoryModel.zip";
        private readonly string _priorityModelPath = "TicketPriorityModel.zip";

        public MlTicketClassifier()
        {
                        _mlContext = new MLContext(seed: 0);
        }

        public void TrainModels(AppDbContext dbContext)
        {
            var tickets = dbContext.Tickets.AsNoTracking().ToList();

            if (tickets.Count < 3) return;

            var allData = tickets.Select(t => new TicketInput
            {
                Title = t.Title,
                Description = t.Description ?? "",
                Category = t.Category.ToString(),
                Priority = t.Priority.ToString()
            }).ToList();

            var dataView = _mlContext.Data.LoadFromEnumerable(allData);

            // --- Category model ---
            var catPipeline = _mlContext.Transforms.Text.FeaturizeText("TitleFeaturized", nameof(TicketInput.Title))
                .Append(_mlContext.Transforms.Text.FeaturizeText("DescFeaturized", nameof(TicketInput.Description)))
                .Append(_mlContext.Transforms.Concatenate("Features", "TitleFeaturized", "DescFeaturized"))
                .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(TicketInput.Category)))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            var catModel = catPipeline.Fit(dataView);
            _mlContext.Model.Save(catModel, dataView.Schema, _categoryModelPath);

            // --- Priority model ---
            var prioPipeline = _mlContext.Transforms.Text.FeaturizeText("TitleFeaturized", nameof(TicketInput.Title))
                .Append(_mlContext.Transforms.Text.FeaturizeText("DescFeaturized", nameof(TicketInput.Description)))
                .Append(_mlContext.Transforms.Concatenate("Features", "TitleFeaturized", "DescFeaturized"))
                .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(TicketInput.Priority)))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            var prioModel = prioPipeline.Fit(dataView);
            _mlContext.Model.Save(prioModel, dataView.Schema, _priorityModelPath);

            // --- Evaluate and save metrics ---
            try
            {
                SaveMetrics(dbContext, "Category", catPipeline, dataView, allData.Count);
                SaveMetrics(dbContext, "Priority", prioPipeline, dataView, allData.Count);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ML Metrics] {ex.Message}");
            }
        }

        private void SaveMetrics(AppDbContext db, string modelType,
            IEstimator<ITransformer> pipeline, IDataView dataView, int sampleCount)
        {
            var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);
            var model = pipeline.Fit(split.TrainSet);
            var predictions = model.Transform(split.TestSet);
            var metrics = _mlContext.MulticlassClassification.Evaluate(predictions);

            db.MlModelMetrics.Add(new MlModelMetrics
            {
                ModelType = modelType,
                TrainedAt = DateTime.UtcNow,
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
                SampleCount = sampleCount
            });
            db.SaveChanges();
        }

                                public (TicketCategory Category, TicketPriority Priority) Predict(string title, string description)
        {
                        if (!File.Exists(_categoryModelPath) || !File.Exists(_priorityModelPath))
            {
                return (TicketCategory.Software, TicketPriority.Normal);
            }

            var input = new TicketInput { Title = title, Description = description };

                        ITransformer categoryModel = _mlContext.Model.Load(_categoryModelPath, out var _);
            var categoryEngine = _mlContext.Model.CreatePredictionEngine<TicketInput, CategoryPrediction>(categoryModel);
            var catPrediction = categoryEngine.Predict(input);

                        ITransformer priorityModel = _mlContext.Model.Load(_priorityModelPath, out var _);
            var priorityEngine = _mlContext.Model.CreatePredictionEngine<TicketInput, PriorityPrediction>(priorityModel);
            var prioPrediction = priorityEngine.Predict(input);

                        Enum.TryParse<TicketCategory>(catPrediction.PredictedCategory, out var category);
            Enum.TryParse<TicketPriority>(prioPrediction.PredictedPriority, out var priority);

            return (category, priority);
        }
    }
}