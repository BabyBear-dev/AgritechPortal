using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models;

public class WeatherData
{
    [Key]
    public int Id { get; set; }

    public int ZoneId { get; set; }

    public decimal Temperature { get; set; }
    public decimal Humidity { get; set; }
    public decimal WindSpeed { get; set; }
    public string Description { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [ForeignKey("ZoneId")]
    public Zone? Zone { get; set; }
}