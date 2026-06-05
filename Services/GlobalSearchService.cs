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
    public class GlobalSearchService
    {
        private readonly AppDbContext _db;
        private const int MaxPerCategory = 8;

        public GlobalSearchService(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<GlobalSearchResults> SearchAsync(string query)
        {
            var result = new GlobalSearchResults();
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
                return result;

            var term = query.Trim().ToLower();

            // Тикеты
            var tickets = await _db.Tickets
                .AsNoTracking()
                .Include(t => t.Client)
                .Where(t => t.Title.ToLower().Contains(term)
                         || (t.Description != null && t.Description.ToLower().Contains(term)))
                .OrderByDescending(t => t.CreatedAt)
                .Take(MaxPerCategory)
                .ToListAsync();

            result.Tickets = tickets.Select(t => new SearchHit
            {
                Kind = SearchHitKind.Ticket,
                Id = t.Id,
                Title = $"#{t.Id}  {t.Title}",
                Snippet = SnippetOf(t.Description, term),
                Meta = $"{t.Status} · {t.Client?.Name ?? "—"} · {t.CreatedAt.ToLocalTime():dd.MM.yyyy}"
            }).ToList();

            // Статьи базы знаний
            var articles = await _db.KnowledgeBase
                .AsNoTracking()
                .Where(a => a.Title.ToLower().Contains(term) || a.Content.ToLower().Contains(term))
                .OrderByDescending(a => a.ViewCount)
                .Take(MaxPerCategory)
                .ToListAsync();

            result.Articles = articles.Select(a => new SearchHit
            {
                Kind = SearchHitKind.Article,
                Id = a.Id,
                Title = a.Title,
                Snippet = SnippetOf(a.Content, term),
                Meta = $"👁 {a.ViewCount} · обновлено {a.UpdatedAt.ToLocalTime():dd.MM.yyyy}"
            }).ToList();

            // Пользователи
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.Username.ToLower().Contains(term)
                         || (u.Email != null && u.Email.ToLower().Contains(term)))
                .Take(MaxPerCategory)
                .ToListAsync();

            result.Users = users.Select(u => new SearchHit
            {
                Kind = SearchHitKind.User,
                Id = u.Id,
                Title = u.Username,
                Snippet = u.Email ?? "",
                Meta = u.Role
            }).ToList();

            // Комментарии
            var comments = await _db.TicketComments
                .AsNoTracking()
                .Include(c => c.Author)
                .Where(c => c.Text.ToLower().Contains(term))
                .OrderByDescending(c => c.CreatedAt)
                .Take(MaxPerCategory)
                .ToListAsync();

            result.Comments = comments.Select(c => new SearchHit
            {
                Kind = SearchHitKind.Comment,
                Id = c.Id,
                LinkedTicketId = c.TicketId,
                Title = $"К тикету #{c.TicketId}",
                Snippet = SnippetOf(c.Text, term),
                Meta = $"{c.Author?.Username ?? "—"} · {c.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            }).ToList();

            return result;
        }

        private static string SnippetOf(string? text, string term)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return text.Length > 120 ? text.Substring(0, 120) + "…" : text;

            var start = Math.Max(0, idx - 40);
            var end = Math.Min(text.Length, idx + term.Length + 80);
            var snippet = text.Substring(start, end - start);
            if (start > 0) snippet = "…" + snippet;
            if (end < text.Length) snippet += "…";
            return snippet;
        }
    }

    public enum SearchHitKind { Ticket, Article, User, Comment }

    public class SearchHit
    {
        public SearchHitKind Kind { get; set; }
        public int Id { get; set; }
        public int? LinkedTicketId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public string Meta { get; set; } = string.Empty;
    }

    public class GlobalSearchResults
    {
        public List<SearchHit> Tickets { get; set; } = new();
        public List<SearchHit> Articles { get; set; } = new();
        public List<SearchHit> Users { get; set; } = new();
        public List<SearchHit> Comments { get; set; } = new();

        public int TotalCount => Tickets.Count + Articles.Count + Users.Count + Comments.Count;
    }
}
