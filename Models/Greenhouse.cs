using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models
{
    public class Greenhouse
    {
        [Key]
        public int Id { get; set; }

        public int FarmId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public double? Width { get; set; }
        public double? Length { get; set; }

        public string ClimateControl { get; set; } = string.Empty;
        public string IrrigationSystem { get; set; } = string.Empty;

        [ForeignKey("FarmId")]
        public Farm? Farm { get; set; }

        public ICollection<Zone> Zones { get; set; } = new List<Zone>();
    }
}