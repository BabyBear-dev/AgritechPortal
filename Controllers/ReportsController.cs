using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Data;
using AgritechPortal.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(AppDbContext db, ILogger<ReportsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ============================================================
    // GET /api/reports/sensor-trend?zoneId=1&type=Temperature&days=7
    // SensorReading has no ZoneId — joins through Device.ZoneId
    // ============================================================
    [HttpGet("sensor-trend")]
    public async Task<IActionResult> SensorTrend(
        [FromQuery] int zoneId,
        [FromQuery] string type,
        [FromQuery] int days = 7)
    {
        try
        {
            var since = DateTime.UtcNow.AddDays(-days);

            var raw = await _db.SensorReadings
                .Include(r => r.Device)
                .Where(r => r.Device != null &&
                            r.Device.ZoneId == zoneId &&
                            r.ReadingType == type &&
                            r.Timestamp >= since &&
                            r.IsValid)
                .OrderBy(r => r.Timestamp)
                .ToListAsync();

            var points = raw.Select(r => new
            {
                timestamp = r.Timestamp,
                value = (double)r.Value,
                unit = r.Unit ?? ""
            }).ToList();

            var values = points.Select(p => p.value).ToList();
            var stats = values.Any() ? new
            {
                min = values.Min(),
                max = values.Max(),
                avg = values.Average(),
                count = values.Count
            } : null;

            return Ok(new
            {
                zoneId,
                type,
                days,
                unit = points.FirstOrDefault()?.unit ?? "",
                stats,
                points
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SensorTrend failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/reports/zone-summary?zoneId=1
    // ============================================================
    [HttpGet("zone-summary")]
    public async Task<IActionResult> ZoneSummary([FromQuery] int zoneId)
    {
        try
        {
            var zone = await _db.Zones
                .Include(z => z.SensorReadings)
                .FirstOrDefaultAsync(z => z.Id == zoneId);

            if (zone == null) return NotFound(new { error = "Zone not found" });

            var readings = zone.SensorReadings?.ToList() ?? new List<SensorReading>();

            var byType = readings
                .GroupBy(r => r.ReadingType)
                .Select(g => new
                {
                    type = g.Key,
                    count = g.Count(),
                    latest = (double)g.OrderByDescending(r => r.Timestamp).First().Value,
                    unit = g.OrderByDescending(r => r.Timestamp).First().Unit ?? "",
                    min = (double)g.Min(r => r.Value),
                    max = (double)g.Max(r => r.Value),
                    avg = (double)g.Average(r => r.Value),
                    firstReading = g.Min(r => r.Timestamp),
                    lastReading = g.Max(r => r.Timestamp)
                })
                .ToList();

            return Ok(new
            {
                zoneId = zone.Id,
                zoneName = zone.Name,
                crop = zone.Crop,
                growthStage = zone.GrowthStage,
                plantingDate = zone.PlantingDate,
                totalReadings = readings.Count,
                types = byType
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/reports/export?zoneId=1&days=30
    // ============================================================
    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv([FromQuery] int zoneId, [FromQuery] int days = 30)
    {
        try
        {
            var since = DateTime.UtcNow.AddDays(-days);

            var readings = await _db.SensorReadings
                .Include(r => r.Device)
                .Where(r => r.Device != null &&
                            r.Device.ZoneId == zoneId &&
                            r.Timestamp >= since)
                .OrderBy(r => r.Timestamp)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,ReadingType,Value,Unit,Source,IsValid");

            foreach (var r in readings)
            {
                sb.AppendLine($"{r.Timestamp:O},{r.ReadingType},{r.Value},{r.Unit},{r.Source},{r.IsValid}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var filename = $"zone-{zoneId}-readings-{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/reports/overview
    // ============================================================
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        try
        {
            var farm = await _db.Farms.FirstOrDefaultAsync();
            if (farm == null) return Ok(new { zoneCount = 0, totalReadings = 0 });

            // ✅ Cast to int? so it matches SensorDevice.ZoneId (nullable int)
            var zones = await _db.Zones
                .Where(z => z.Greenhouse != null && z.Greenhouse.FarmId == farm.Id)
                .Select(z => (int?)z.Id)
                .ToListAsync();

            // Count readings where the device belongs to one of our zones
            var totalReadings = await _db.SensorReadings
                .Include(r => r.Device)
                .Where(r => r.Device != null && zones.Contains(r.Device.ZoneId))
                .CountAsync();

            var recentReadings = await _db.SensorReadings
                .Include(r => r.Device)
                .Where(r => r.Device != null &&
                            zones.Contains(r.Device.ZoneId) &&
                            r.Timestamp >= DateTime.UtcNow.AddDays(-1))
                .CountAsync();

            // Recommendation.ZoneId is a non-nullable int — implicit conversion works fine
            var recommendationCount = await _db.Recommendations
                .Where(r => zones.Contains(r.ZoneId))
                .CountAsync();

            var approvedCount = await _db.Recommendations
                .Where(r => zones.Contains(r.ZoneId) && r.Status == "approved")
                .CountAsync();

            return Ok(new
            {
                farmName = farm.Name,
                zoneCount = zones.Count,
                totalReadings,
                readingsLast24h = recentReadings,
                recommendationCount,
                approvedCount,
                approvalRate = recommendationCount > 0 ? (double)approvedCount / recommendationCount * 100 : 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overview failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}