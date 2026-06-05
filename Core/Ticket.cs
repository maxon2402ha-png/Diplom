using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace КР_Ханников.Core
{
    public class Ticket
    {
        public static readonly string[] AllStatuses =
        {
            Constants.TicketStatus.Open,
            Constants.TicketStatus.InProgress,
            Constants.TicketStatus.Closed
        };

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Status { get; set; } = Constants.TicketStatus.Open;

        public TicketPriority Priority { get; set; } = TicketPriority.Normal;
        public TicketCategory Category { get; set; } = TicketCategory.General;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

                [NotMapped]
        public DateTime? UpdatedAt { get; set; }

        public DateTime? DueAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        [MaxLength(500)]
        public string? AttachmentPath { get; set; }

        [MaxLength(255)]
        public string? AttachmentFileName { get; set; }

        [MaxLength(100)]
        public string? AttachmentContentType { get; set; }

        public long? AttachmentSize { get; set; }

        public int? AssigneeEmployeeId { get; set; }
        public Employee? Assignee { get; set; }

        public ICollection<TicketHistory> History { get; set; } = new List<TicketHistory>();

        public Solution? Solution { get; set; }

        public Feedback? Feedback { get; set; }

        public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();

        public int ClientId { get; set; }
        public Client Client { get; set; } = null!;

     
        [NotMapped]
        public int? UserId { get; set; }

        [NotMapped]
        public User? User { get; set; }

        public bool IsOverdue { get; set; } = false;

        public bool NotifiedAboutDeadline { get; set; } = false;

        public DateTime? ClientConfirmationDeadline { get; set; }

        public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();

        [NotMapped]
        public bool IsCurrentlyOverdue =>
            DueAt.HasValue && DueAt.Value < DateTime.UtcNow && Status != Constants.TicketStatus.Closed;
    }
}