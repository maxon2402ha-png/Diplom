using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace КР_Ханников.Data
{
    public class KnowledgeArticle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public int AuthorId { get; set; }

        [ForeignKey("AuthorId")]

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int ViewCount { get; set; } = 0;
        public int HelpfulCount { get; set; } = 0;
        public int NotHelpfulCount { get; set; } = 0;

        // Опубликована ли статья. Клиенты видят только опубликованные;
        // черновики (false) доступны только сотрудникам и администраторам.
        public bool IsPublished { get; set; } = true;
    }
}