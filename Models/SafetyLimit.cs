using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class SafetyLimit
    {
        [Key]
        public int Id { get; set; }

        public int FarmId { get; set; }

        public double? TempMin { get; set; }
        public double? TempMax { get; set; }
        public double? PhMin { get; set; }
        public double? PhMax { get; set; }

        public string EmergencyStop { get; set; } = "enabled";

        [ForeignKey("FarmId")]
        public Farm? Farm { get; set; }
    }
}