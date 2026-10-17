using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class ActionLog
    {
        [Key]
        public int Id { get; set; }

        // ✅ NEW — which actuator this log entry is about
        public int? ActuatorId { get; set; }

        public int? OperatorId { get; set; }
        public int? ZoneId { get; set; }

        [Required]
        public string Action { get; set; } = string.Empty;

        public string Details { get; set; } = string.Empty;

        // ✅ NEW — what triggered it: "manual", "automation", "ai", "schedule"
        [MaxLength(50)]
        public string? Source { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [ForeignKey("OperatorId")]
        public Operator? Operator { get; set; }

        [ForeignKey("ZoneId")]
        public Zone? Zone { get; set; }
    }
}