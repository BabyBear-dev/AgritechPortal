using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Data;
using AgritechPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ZonesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ZonesController> _logger;

    public ZonesController(AppDbContext db, ILogger<ZonesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ============================================================
    // GET /api/zones
    // ============================================================
    [HttpGet]
    public async Task<IActionResult> ListAll()
    {
        try
        {
            var farm = await _db.Farms.FirstOrDefaultAsync();
            if (farm == null)
                return Ok(new { farmName = (string?)null, zones = Array.Empty<object>() });

            var greenhouses = await _db.Greenhouses
                .Where(g => g.FarmId == farm.Id)
                .OrderBy(g => g.Id)
                .ToListAsync();

            var result = new List<object>();

            foreach (var gh in greenhouses)
            {
                // Load zones WITH their sensor readings via navigation property
                var zones = await _db.Zones
                    .Include(z => z.SensorReadings)
                    .Where(z => z.GreenhouseId == gh.Id)
                    .OrderBy(z => z.Id)
                    .ToListAsync();

                var zonePayloads = new List<object>();

                foreach (var z in zones)
                {
                    var readings = z.SensorReadings
                        .OrderByDescending(r => r.Timestamp)
                        .Take(20)
                        .ToList();

                    var latestByType = readings
                        .GroupBy(r => r.ReadingType)
                        .Select(g => g.First())
                        .ToDictionary(
                            r => r.ReadingType,
                            r => new
                            {
                                value = r.Value,
                                unit = r.Unit,
                                timestamp = r.Timestamp,
                                isValid = r.IsValid
                            });

                    int healthScore = ComputeHealth(readings);

                    zonePayloads.Add(new
                    {
                        id = z.Id,
                        name = z.Name,
                        crop = z.Crop,
                        growthStage = z.GrowthStage,
                        area = z.Area,
                        plantingDate = z.PlantingDate,
                        greenhouseId = z.GreenhouseId,
                        healthScore,
                        latestReadings = latestByType,
                        readingCount = readings.Count,
                        lastReadingAt = readings.FirstOrDefault()?.Timestamp
                    });
                }

                result.Add(new
                {
                    greenhouseId = gh.Id,
                    greenhouseName = gh.Name,
                    climateControl = gh.ClimateControl,
                    irrigationSystem = gh.IrrigationSystem,
                    length = gh.Length,
                    width = gh.Width,
                    zones = zonePayloads
                });
            }

            return Ok(new
            {
                farmName = farm.Name,
                farmId = farm.Id,
                greenhouseCount = greenhouses.Count,
                zones = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListAll zones failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/zones/{id}
    // ============================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> GetZone(int id)
    {
        try
        {
            var zone = await _db.Zones
                .Include(z => z.Greenhouse)
                .Include(z => z.SensorReadings)
                .FirstOrDefaultAsync(z => z.Id == id);

            if (zone == null) return NotFound(new { error = "Zone not found" });

            var readings = zone.SensorReadings
                .OrderByDescending(r => r.Timestamp)
                .Take(100)
                .ToList();

            var latestByType = readings
                .GroupBy(r => r.ReadingType)
                .Select(g => g.First())
                .ToList();

            return Ok(new
            {
                id = zone.Id,
                name = zone.Name,
                crop = zone.Crop,
                growthStage = zone.GrowthStage,
                area = zone.Area,
                plantingDate = zone.PlantingDate,
                greenhouseId = zone.GreenhouseId,
                greenhouseName = zone.Greenhouse?.Name,
                latestReadings = latestByType.Select(r => new
                {
                    type = r.ReadingType,
                    value = r.Value,
                    unit = r.Unit,
                    timestamp = r.Timestamp,
                    isValid = r.IsValid,
                    source = r.Source
                }),
                totalReadings = readings.Count
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // POST /api/zones/{id}/readings
    // ============================================================
    [HttpPost("{id}/readings")]
    public async Task<IActionResult> AddReading(int id, [FromBody] ReadingDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Type))
            return BadRequest(new { error = "Reading type and value are required" });

        var zone = await _db.Zones
            .Include(z => z.SensorReadings)
            .FirstOrDefaultAsync(z => z.Id == id);

        if (zone == null) return NotFound(new { error = "Zone not found" });

        try
        {
            var reading = new SensorReading
            {
                ReadingType = dto.Type.Trim(),
                Value = dto.Value,
                Unit = string.IsNullOrWhiteSpace(dto.Unit) ? DefaultUnitFor(dto.Type) : dto.Unit,
                Timestamp = DateTime.UtcNow,
                IsValid = true,
                Source = dto.Source ?? "manual"
            };

            // Attach via the navigation property — EF links it to the zone automatically
            zone.SensorReadings.Add(reading);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Reading saved", id = reading.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddReading failed for zone {ZoneId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DELETE /api/zones/{id}/readings (wipe)
    // ============================================================
    [HttpDelete("{id}/readings")]
    public async Task<IActionResult> ClearReadings(int id)
    {
        try
        {
            var zone = await _db.Zones
                .Include(z => z.SensorReadings)
                .FirstOrDefaultAsync(z => z.Id == id);

            if (zone == null) return NotFound(new { error = "Zone not found" });

            var count = zone.SensorReadings.Count;
            _db.SensorReadings.RemoveRange(zone.SensorReadings);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Removed {count} readings" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // Helpers
    // ============================================================
    private static string DefaultUnitFor(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "temperature" or "temp" => "°C",
            "humidity" => "%",
            "soilmoisture" or "soil_moisture" => "%",
            "ph" => "pH",
            "light" or "lightlevel" => "lux",
            "co2" => "ppm",
            "ec" => "mS/cm",
            _ => ""
        };
    }

    private static int ComputeHealth(List<SensorReading> readings)
    {
        int score = 100;

        var latest = readings
            .GroupBy(r => r.ReadingType)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .ToList();

        double? temp = null, hum = null, soil = null;

        foreach (var r in latest)
        {
            double v = (double)r.Value;
            switch (r.ReadingType)
            {
                case "Temperature": temp = v; break;
                case "Humidity": hum = v; break;
                case "SoilMoisture": soil = v; break;
            }
        }

        if (temp.HasValue)
        {
            if (temp < 10 || temp > 35) score -= 30;
            else if (temp < 15 || temp > 30) score -= 15;
        }

        if (hum.HasValue)
        {
            if (hum < 30 || hum > 85) score -= 25;
            else if (hum < 40 || hum > 75) score -= 10;
        }

        if (soil.HasValue)
        {
            if (soil < 20 || soil > 90) score -= 30;
            else if (soil < 30 || soil > 75) score -= 10;
        }

        return Math.Max(0, Math.Min(100, score));
    }

    public class ReadingDto
    {
        public string Type { get; set; } = "";
        public decimal Value { get; set; }
        public string? Unit { get; set; }
        public string? Source { get; set; }
    }
}