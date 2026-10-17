using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class Actuator
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string DeviceName { get; set; } = string.Empty;

        // ✅ NEW — what kind of actuator (Pump, Fan, Light, Valve, Heater, etc.)
        [MaxLength(50)]
        public string Type { get; set; } = "Generic";

        // ✅ NEW — current on/off state
        public bool IsOn { get; set; } = false;

        // ✅ NEW — last time the state changed
        public DateTime? LastChangedAt { get; set; }

        public bool IsOnline { get; set; } = true;

        public int? ZoneId { get; set; }

        [ForeignKey("ZoneId")]
        public Zone? Zone { get; set; }
    }
}