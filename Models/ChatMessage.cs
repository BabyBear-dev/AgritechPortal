using System;
using System.ComponentModel.DataAnnotations;

namespace AgritechPortal.Models
{
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Who sent the message. "user" or "ai"</summary>
        [Required, MaxLength(10)]
        public string Sender { get; set; } = "user";

        [Required]
        public string Content { get; set; } = string.Empty;

        /// <summary>Email of the operator who owns this message.</summary>
        [Required, MaxLength(200)]
        public string OperatorEmail { get; set; } = string.Empty;

        /// <summary>Groups messages into a conversation thread.</summary>
        [MaxLength(64)]
        public string? ConversationId { get; set; }

        /// <summary>Optional zone context for this message.</summary>
        public int? ZoneId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>JSON array of source tags the AI cited, e.g. ["groq","weather"]</summary>
        public string? Sources { get; set; }
    }
}