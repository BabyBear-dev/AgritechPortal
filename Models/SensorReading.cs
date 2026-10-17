using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgritechPortal.Models;

public class SensorReading
{
    [Key]
    public int Id { get; set; }

    // ✅ Made nullable – allows existing rows with 0 to be migrated
    public int? DeviceId { get; set; }

    public string ReadingType { get; set; } = string.Empty; // temperature, humidity, pH, EC, CO2, light, moisture

    public decimal Value { get; set; }

    public string? Unit { get; set; } // °C, %, µS/cm, ppm, lux, mmol

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public bool IsValid { get; set; } = true;

    public string? Source { get; set; } // sensor, manual, weather_api

    [ForeignKey("DeviceId")]
    public SensorDevice? Device { get; set; }
}