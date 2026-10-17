using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Services;
using AgritechPortal.Data;
using AgritechPortal.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HarvestController : ControllerBase
{
    private readonly PredictiveHarvestService _harvestService;
    private readonly AIDecisionEngine _aiEngine;
    private readonly AppDbContext _db;
    private readonly WeatherApiService _weatherService;

    public HarvestController(
        PredictiveHarvestService harvestService,
        AIDecisionEngine aiEngine,
        AppDbContext db,
        WeatherApiService weatherService)
    {
        _harvestService = harvestService;
        _aiEngine = aiEngine;
        _db = db;
        _weatherService = weatherService;
    }

    // ============================================================
    // 🌾 PREDICTION — never throws, always returns 200 with a shape
    // ============================================================
    [HttpGet("predict/{zoneId}")]
    public async Task<IActionResult> GetPrediction(int zoneId)
    {
        try
        {
            var prediction = await _harvestService.GetPredictionAsync(zoneId);
            if (prediction == null)
            {
                return Ok(new
                {
                    zoneId,
                    zoneName = $"Zone {zoneId}",
                    cropName = "Unknown",
                    growthPercent = 0,
                    daysToHarvest = 0,
                    estimatedYieldKg = 0,
                    confidence = 0,
                    growthRate = 0,
                    fruitDevelopment = 0,
                    riskFactors = new[] { "No prediction data available yet" },
                    note = "Add sensor readings for this zone to generate a real prediction."
                });
            }
            return Ok(prediction);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ predict/{zoneId} failed: {ex.Message}");
            return Ok(new
            {
                zoneId,
                zoneName = $"Zone {zoneId}",
                cropName = "Unknown",
                growthPercent = 0,
                daysToHarvest = 0,
                estimatedYieldKg = 0,
                confidence = 0,
                growthRate = 0,
                fruitDevelopment = 0,
                riskFactors = new[] { $"Prediction error: {ex.Message}" }
            });
        }
    }

    // ============================================================
    // 📊 EVIDENCE — for a recommendation
    // ============================================================
    [HttpGet("evidence/{zoneId}")]
    public async Task<IActionResult> GetEvidence(int zoneId)
    {
        try
        {
            var rec = await _db.Recommendations
                .Where(r => r.ZoneId == zoneId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (rec == null)
                return Ok(new { evidence = new string[] { }, reasoning = "No recommendations yet." });

            var evidence = new List<string>();
            var reasoning = "";
            if (!string.IsNullOrEmpty(rec.ActionParams))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(rec.ActionParams);
                    if (parsed != null)
                    {
                        if (parsed.TryGetValue("evidence", out var ev) && ev != null)
                            evidence = JsonSerializer.Deserialize<List<string>>(ev.ToString() ?? "[]") ?? new List<string>();
                        if (parsed.TryGetValue("reasoning", out var re) && re != null)
                            reasoning = re.ToString() ?? "";
                    }
                }
                catch { }
            }
            return Ok(new { evidence, reasoning });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ============================================================
    // 📋 CREATE HARVEST PLAN — generates and approves a recommendation
    // ============================================================
    [HttpPost("plan/{zoneId}")]
    public async Task<IActionResult> CreateHarvestPlan(int zoneId)
    {
        try
        {
            var rec = await _aiEngine.GenerateRecommendationAsync(zoneId);
            if (rec == null)
                return BadRequest(new { error = "AI engine returned null – check server logs." });

            rec.Status = "approved";
            await _db.SaveChangesAsync();
            return Ok(new { message = "Plan created!", recommendation = rec });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    // ============================================================
    // 🌿 ZONE STATUS
    // ============================================================
    [HttpGet("zone/{zoneId}/status")]
    public async Task<IActionResult> GetZoneStatus(int zoneId)
    {
        try
        {
            var zone = await _db.Zones
                .Include(z => z.SensorReadings)
                .FirstOrDefaultAsync(z => z.Id == zoneId);
            if (zone == null) return NotFound();

            var latestRec = await _db.Recommendations
                .Where(r => r.ZoneId == zoneId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            var latestReading = zone.SensorReadings
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefault();

            return Ok(new
            {
                zoneId = zone.Id,
                zoneName = zone.Name,
                crop = zone.Crop,
                growthStage = zone.GrowthStage,
                plantingDate = zone.PlantingDate,
                hasPendingRecommendation = latestRec?.Status == "pending",
                latestRecommendation = latestRec,
                latestReading = latestReading,
                readingCount = zone.SensorReadings.Count
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ============================================================
    // 📋 LIST ZONES — used by frontend to pick a default zone
    // ============================================================
    [HttpGet("list")]
    public async Task<IActionResult> ListZones()
    {
        try
        {
            var zones = await _db.Zones
                .Select(z => new { z.Id, z.Name, z.Crop })
                .ToListAsync();
            return Ok(zones);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ list failed: {ex.Message}");
            return Ok(Array.Empty<object>());
        }
    }

    // ============================================================
    // 🌤️ TEST WEATHER — debug endpoint
    // ============================================================
    [HttpGet("test-weather")]
    public async Task<IActionResult> TestWeather()
    {
        var farm = await _db.Farms.FirstOrDefaultAsync();
        if (farm == null || !farm.Latitude.HasValue || !farm.Longitude.HasValue)
            return BadRequest("No farm with coordinates found.");

        var weather = await _weatherService.GetCurrentWeatherAsync(
            farm.Latitude.Value,
            farm.Longitude.Value
        );
        return Ok(weather);
    }

    // ============================================================
    // 📍 SAVE FARM LOCATION
    // ============================================================
    [HttpPost("location")]
    public async Task<IActionResult> SaveLocation([FromBody] SaveLocationDto dto)
    {
        if (dto == null)
            return BadRequest(new { message = "Request body is required." });

        if (dto.Latitude < -90 || dto.Latitude > 90)
            return BadRequest(new { message = "Latitude must be between -90 and 90." });

        if (dto.Longitude < -180 || dto.Longitude > 180)
            return BadRequest(new { message = "Longitude must be between -180 and 180." });

        try
        {
            // Single-tenant: use the first farm
            var farm = await _db.Farms.FirstOrDefaultAsync();

            if (farm == null)
            {
                farm = new Farm
                {
                    Name = "My Farm",
                    Latitude = dto.Latitude,
                    Longitude = dto.Longitude,
                    Address = dto.Address,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Farms.Add(farm);
                Console.WriteLine($"📍 Created new farm with coords ({dto.Latitude:F4}, {dto.Longitude:F4})");
            }
            else
            {
                farm.Latitude = dto.Latitude;
                farm.Longitude = dto.Longitude;
                farm.Address = dto.Address;
                Console.WriteLine($"📍 Updated farm '{farm.Name}' to ({dto.Latitude:F4}, {dto.Longitude:F4})");
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"Location saved: {dto.Latitude:F4}, {dto.Longitude:F4}",
                latitude = farm.Latitude,
                longitude = farm.Longitude,
                address = farm.Address
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SaveLocation failed: {ex.Message}");
            return StatusCode(500, new { message = $"Server error: {ex.Message}" });
        }
    }

    // ============================================================
    // 📍 GET CURRENT FARM LOCATION
    // ============================================================
    [HttpGet("location")]
    public async Task<IActionResult> GetLocation()
    {
        try
        {
            var farm = await _db.Farms.FirstOrDefaultAsync();
            if (farm == null)
                return Ok(new
                {
                    latitude = (double?)null,
                    longitude = (double?)null,
                    address = (string?)null,
                    name = (string?)null
                });

            return Ok(new
            {
                latitude = farm.Latitude,
                longitude = farm.Longitude,
                address = farm.Address,
                name = farm.Name
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetLocation failed: {ex.Message}");
            return StatusCode(500, new { message = $"Server error: {ex.Message}" });
        }
    }

    // ============================================================
    // DTO
    // ============================================================
    public class SaveLocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Address { get; set; }
    }
}