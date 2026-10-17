using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class Zone
    {
        [Key]
        public int Id { get; set; }

        public int GreenhouseId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Crop { get; set; } = string.Empty;

        public DateTime? PlantingDate { get; set; }

        public string GrowthStage { get; set; } = string.Empty;

        public double? Area { get; set; }

        public string TargetRanges { get; set; } = string.Empty;

        [ForeignKey("GreenhouseId")]
        public Greenhouse? Greenhouse { get; set; }

        public ICollection<SensorReading> SensorReadings { get; set; } = new List<SensorReading>();
        public ICollection<Actuator> Actuators { get; set; } = new List<Actuator>();
        public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
        public ICollection<ActionLog> ActionLogs { get; set; } = new List<ActionLog>();
        public ICollection<FarmMemory> FarmMemories { get; set; } = new List<FarmMemory>();
    }
}