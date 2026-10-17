using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class SensorTelemetry
    {
        [Key]
        public int Id { get; set; }

        public int OperatorId { get; set; }

        public double Nitrogen { get; set; }
        public double Phosphorus { get; set; }
        public double Potassium { get; set; }
        public double SoilMoisture { get; set; }
        public double SoilPh { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("OperatorId")]
        public Operator? Operator { get; set; }
    }
}