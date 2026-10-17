using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class Recommendation
    {
        [Key]
        public int Id { get; set; }

        public int? ZoneId { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string ActionParams { get; set; } = string.Empty; // JSON with evidence & reasoning

        public int Confidence { get; set; }

        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ❌ REMOVE these – they don't exist in the database:
        // public List<string> Evidence { get; set; }
        // public string Reasoning { get; set; }
        // public DateTime? ApprovedAt { get; set; }

        [ForeignKey("ZoneId")]
        public Zone? Zone { get; set; }
    }
}