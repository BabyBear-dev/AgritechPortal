using System;
using System.ComponentModel.DataAnnotations;

namespace AgritechPortal.Models
{
    public class Operator
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Role { get; set; } = "Owner";

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public string? ResetToken { get; set; }

        public DateTime? ResetTokenExpires { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}