using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace КР_Ханников.Core
{
    public class TicketAttachment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string StoredFilePath { get; set; } = string.Empty;

        [MaxLength(100)]
        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public int TicketId { get; set; }
        public Ticket Ticket { get; set; } = null!;

        public int UploadedByUserId { get; set; }
        public User UploadedByUser { get; set; } = null!;
    }
}
