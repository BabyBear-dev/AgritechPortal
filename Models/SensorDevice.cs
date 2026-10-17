using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models;

public class SensorDevice
{
    [Key]
    public int Id { get; set; }

    public int ZoneId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string DeviceType { get; set; } = string.Empty; // temperature, humidity, pH, EC, CO2, light, camera

    public string? SerialNumber { get; set; }

    public string? FirmwareVersion { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastSeen { get; set; }

    public string Status { get; set; } = "online"; // online, offline, error

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("ZoneId")]
    public Zone? Zone { get; set; }

    public ICollection<SensorReading> SensorReadings { get; set; } = new List<SensorReading>();
}