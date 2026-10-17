using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class ActuatorRelay
    {
        [Key]
        public int Id { get; set; }

        public int OperatorId { get; set; }  // from logs

        [Required]
        public string DeviceName { get; set; } = string.Empty;

        public bool IsOnline { get; set; } = true;

        public DateTime? StatusUpdatedAt { get; set; }

        [ForeignKey("OperatorId")]
        public Operator? Operator { get; set; }
    }
}