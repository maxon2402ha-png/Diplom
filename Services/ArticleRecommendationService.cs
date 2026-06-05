using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using КР_Ханников.Data;

namespace КР_Ханников.Services
{
    [SupportedOSPlatform("windows")]
    public class ArticleRecommendationService
    {
        private readonly AppDbContext _db;

        // Минимальный список русских/английских стоп-слов
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "и","в","на","с","не","что","по","для","из","к","от","о","а","но","или","же","бы",
            "это","как","так","быть","есть","нет","да","за","до","под","над","при","через",
            "the","a","an","is","are","was","were","be","been","being","of","to","in","on","at",
            "for","with","by","from","as","that","this","it","its","and","or","but","if","then"
        };

        private static readonly char[] Separators =
        {
            ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')',
            '[', ']', '{', '}', '"', '\'', '-', '/', '\\', '|', '*', '&', '@', '#', '%'
        };

        public ArticleRecommendationService(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<RecommendedArticle>> RecommendAsync(
            string ticketTitle, string ticketDescription, int topN = 5)
        {
            var articles = await _db.KnowledgeBase
                .AsNoTracking()
                .ToListAsync();

            if (articles.Count == 0) return new List<RecommendedArticle>();

            var query = $"{ticketTitle} {ticketDescription}";
            var queryTokens = Tokenize(query);
            if (queryTokens.Count == 0) return new List<RecommendedArticle>();

            // Подготавливаем документы: каждая статья = title + content
            var docs = articles.Select(a => new
            {
                Article = a,
                Tokens = Tokenize($"{a.Title} {a.Content}")
            }).Where(d => d.Tokens.Count > 0).ToList();

            if (docs.Count == 0) return new List<RecommendedArticle>();

            // IDF (по корпусу всех статей)
            var docCount = docs.Count;
            var df = new Dictionary<string, int>();
            foreach (var d in docs)
            {
                foreach (var term in d.Tokens.Distinct())
                {
                    df.TryGetValue(term, out var cnt);
                    df[term] = cnt + 1;
                }
            }
            var idf = df.ToDictionary(
                kv => kv.Key,
                kv => Math.Log((double)docCount / kv.Value) + 1.0);

            // TF-IDF вектор для запроса
            var queryVec = ToTfIdfVector(queryTokens, idf);

            // Считаем cosine similarity
            var scored = new List<RecommendedArticle>();
            foreach (var d in docs)
            {
                var docVec = ToTfIdfVector(d.Tokens, idf);
                var sim = CosineSimilarity(queryVec, docVec);
                if (sim > 0.01)
                {
                    scored.Add(new RecommendedArticle
                    {
                        Article = d.Article,
                        Score = sim
                    });
                }
            }

            return scored
                .OrderByDescending(s => s.Score)
                .Take(topN)
                .ToList();
        }

        private static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text
                .ToLowerInvariant()
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2 && !StopWords.Contains(t))
                .ToList();
        }

        private static Dictionary<string, double> ToTfIdfVector(
            List<string> tokens, Dictionary<string, double> idf)
        {
            var tf = new Dictionary<string, int>();
            foreach (var t in tokens)
            {
                tf.TryGetValue(t, out var c);
                tf[t] = c + 1;
            }

            var len = tokens.Count;
            var vec = new Dictionary<string, double>();
            foreach (var kv in tf)
            {
                var termIdf = idf.GetValueOrDefault(kv.Key, 0.0);
                vec[kv.Key] = ((double)kv.Value / len) * termIdf;
            }
            return vec;
        }

        private static double CosineSimilarity(
            Dictionary<string, double> a, Dictionary<string, double> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;

            double dot = 0;
            foreach (var kv in a)
            {
                if (b.TryGetValue(kv.Key, out var bv))
                    dot += kv.Value * bv;
            }

            double normA = Math.Sqrt(a.Values.Sum(v => v * v));
            double normB = Math.Sqrt(b.Values.Sum(v => v * v));

            if (normA == 0 || normB == 0) return 0;
            return dot / (normA * normB);
        }
    }

    public class RecommendedArticle
    {
        public Data.KnowledgeArticle Article { get; set; } = null!;
        public double Score { get; set; }
        public string ScorePercent => $"{Score * 100:F0}%";
    }
}
