using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    [Table("farmmemories")]
    public class FarmMemory
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("zone_id")]
        public int? ZoneId { get; set; }

        [Column("created_at")]
        public DateTime Date { get; set; } = DateTime.UtcNow;

        [Column("observation")]
        public string Observation { get; set; } = string.Empty;

        [Column("decision")]
        public string Decision { get; set; } = string.Empty;

        [Column("outcome")]
        public string Outcome { get; set; } = string.Empty;

        [Column("yield_impact")]
        public string YieldImpact { get; set; } = string.Empty;

        [Column("source")]
        public string Source { get; set; } = "chat";

        [Column("chat_message_id")]
        public int? ChatMessageId { get; set; }

        [ForeignKey("ZoneId")]
        public Zone? Zone { get; set; }
    }
}