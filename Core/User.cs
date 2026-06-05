using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace КР_Ханников.Core
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = Constants.UserRoles.Client;

                                public string? AvatarPath { get; set; }

                                [MaxLength(100)]
        public string? Email { get; set; }

                                public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

                                public DateTime? LastLoginAt { get; set; }

        
                                public bool IsEmailVerified { get; set; } = false;

        [MaxLength(10)]
        public string? VerificationCode { get; set; }

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockedUntil { get; set; }

        public bool MustChangePassword { get; set; } = false;

        [NotMapped]
        public Employee? Employee { get; set; }

        [NotMapped]
        public Client? Client { get; set; }
    }
}