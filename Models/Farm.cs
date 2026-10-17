using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class Farm
    {
        [Key]
        public int Id { get; set; }

        public int OperatorId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public double? Area { get; set; }

        public string PrimaryCrop { get; set; } = string.Empty;

        // ===== Farm location for weather API =====
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // ===== Human-readable address from reverse geocoding =====
        [MaxLength(500)]
        public string? Address { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("OperatorId")]
        public Operator? Operator { get; set; }

        public ICollection<Greenhouse> Greenhouses { get; set; } = new List<Greenhouse>();

        public SafetyLimit? SafetyLimit { get; set; }
    }
}