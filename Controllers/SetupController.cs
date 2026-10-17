using Microsoft.AspNetCore.Mvc;
using AgritechPortal.Data;
using AgritechPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SetupController : ControllerBase
{
    private readonly AppDbContext _db;

    public SetupController(AppDbContext db)
    {
        _db = db;
    }

    // ============================================================
    // GET /api/setup/status
    // Returns whether the farm is fully set up
    // ============================================================
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var farm = await _db.Farms.FirstOrDefaultAsync();
            if (farm == null)
                return Ok(new { step = 1, farmExists = false, greenhouseCount = 0, zoneCount = 0 });

            var greenhouseCount = await _db.Greenhouses.CountAsync(g => g.FarmId == farm.Id);
            if (greenhouseCount == 0)
                return Ok(new { step = 2, farmExists = true, farmId = farm.Id, greenhouseCount = 0, zoneCount = 0 });

            var zoneCount = await _db.Zones
                .Where(z => z.Greenhouse != null && z.Greenhouse.FarmId == farm.Id)
                .CountAsync();

            if (zoneCount == 0)
                return Ok(new { step = 3, farmExists = true, farmId = farm.Id, greenhouseCount, zoneCount = 0 });

            return Ok(new
            {
                step = 4,
                complete = true,
                farmExists = true,
                farmId = farm.Id,
                greenhouseCount,
                zoneCount
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/setup/farm
    // ============================================================
    [HttpGet("farm")]
    public async Task<IActionResult> GetFarm()
    {
        var farm = await _db.Farms.FirstOrDefaultAsync();
        if (farm == null) return Ok(new { exists = false });
        return Ok(new { exists = true, farm });
    }

    // ============================================================
    // POST /api/setup/farm
    // Creates or updates the farm
    // ============================================================
    [HttpPost("farm")]
    public async Task<IActionResult> SaveFarm([FromBody] FarmSetupDto dto)
    {
        if (dto == null) return BadRequest(new { error = "Body required" });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Farm name is required" });

        try
        {
            var email = dto.OperatorEmail;
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { error = "Operator email missing — please log in again." });

            var op = await _db.Operators.FirstOrDefaultAsync(o => o.Email == email);
            if (op == null)
                return BadRequest(new { error = $"No operator found with email {email}. Please register first." });

            var farm = await _db.Farms.FirstOrDefaultAsync();

            if (farm == null)
            {
                farm = new Farm { OperatorId = op.Id, CreatedAt = DateTime.UtcNow };
                _db.Farms.Add(farm);
            }

            farm.Name = dto.Name.Trim();
            farm.Type = dto.Type ?? "Greenhouse";
            farm.Area = dto.Area;
            farm.PrimaryCrop = dto.PrimaryCrop ?? "";
            if (dto.Latitude.HasValue) farm.Latitude = dto.Latitude;
            if (dto.Longitude.HasValue) farm.Longitude = dto.Longitude;
            if (!string.IsNullOrWhiteSpace(dto.Address)) farm.Address = dto.Address;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Farm saved", farmId = farm.Id });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SaveFarm: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/setup/greenhouses
    // ============================================================
    [HttpGet("greenhouses")]
    public async Task<IActionResult> ListGreenhouses()
    {
        var farm = await _db.Farms.FirstOrDefaultAsync();
        if (farm == null) return Ok(Array.Empty<object>());

        var list = await _db.Greenhouses
            .Where(g => g.FarmId == farm.Id)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Length,
                g.Width,
                g.ClimateControl,
                g.IrrigationSystem,
                zoneCount = g.Zones != null ? g.Zones.Count : 0
            })
            .ToListAsync();

        return Ok(list);
    }

    // ============================================================
    // POST /api/setup/greenhouse
    // ============================================================
    [HttpPost("greenhouse")]
    public async Task<IActionResult> AddGreenhouse([FromBody] GreenhouseSetupDto dto)
    {
        if (dto == null) return BadRequest(new { error = "Body required" });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Greenhouse name is required" });

        var farm = await _db.Farms.FirstOrDefaultAsync();
        if (farm == null) return BadRequest(new { error = "Create the farm first (step 1)." });

        try
        {
            var gh = new Greenhouse
            {
                FarmId = farm.Id,
                Name = dto.Name.Trim(),
                Length = dto.Length,
                Width = dto.Width,
                ClimateControl = string.IsNullOrWhiteSpace(dto.ClimateControl) ? "Manual" : dto.ClimateControl,
                IrrigationSystem = string.IsNullOrWhiteSpace(dto.IrrigationSystem) ? "Manual" : dto.IrrigationSystem
            };

            _db.Greenhouses.Add(gh);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Greenhouse added", id = gh.Id });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ AddGreenhouse: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DELETE /api/setup/greenhouse/{id}
    // ============================================================
    [HttpDelete("greenhouse/{id}")]
    public async Task<IActionResult> DeleteGreenhouse(int id)
    {
        try
        {
            var gh = await _db.Greenhouses.FindAsync(id);
            if (gh == null) return NotFound();

            // Check for zones
            var zones = await _db.Zones.Where(z => z.GreenhouseId == id).ToListAsync();
            if (zones.Any())
                return BadRequest(new { error = $"Cannot delete: {zones.Count} zone(s) still attached." });

            _db.Greenhouses.Remove(gh);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Greenhouse deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // GET /api/setup/greenhouse/{id}/zones
    // ============================================================
    [HttpGet("greenhouse/{id}/zones")]
    public async Task<IActionResult> ListZones(int id)
    {
        var zones = await _db.Zones
            .Where(z => z.GreenhouseId == id)
            .Select(z => new
            {
                z.Id,
                z.Name,
                z.Area,
                z.Crop,
                z.GrowthStage,
                z.PlantingDate
            })
            .ToListAsync();

        return Ok(zones);
    }

    // ============================================================
    // POST /api/setup/zone
    // ============================================================
    [HttpPost("zone")]
    public async Task<IActionResult> AddZone([FromBody] ZoneSetupDto dto)
    {
        if (dto == null) return BadRequest(new { error = "Body required" });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Zone name is required" });
        if (dto.GreenhouseId <= 0) return BadRequest(new { error = "Greenhouse ID required" });

        var gh = await _db.Greenhouses.FindAsync(dto.GreenhouseId);
        if (gh == null) return BadRequest(new { error = "Greenhouse not found" });

        try
        {
            var zone = new Zone
            {
                GreenhouseId = dto.GreenhouseId,
                Name = dto.Name.Trim(),
                Area = dto.Area,
                Crop = string.IsNullOrWhiteSpace(dto.Crop) ? "Tomato" : dto.Crop,
                GrowthStage = string.IsNullOrWhiteSpace(dto.GrowthStage) ? "Seedling" : dto.GrowthStage,
                PlantingDate = dto.PlantingDate ?? DateTime.UtcNow,
                TargetRanges = dto.TargetRanges ?? "{}"
            };

            _db.Zones.Add(zone);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Zone added", id = zone.Id });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ AddZone: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DELETE /api/setup/zone/{id}
    // ============================================================
    [HttpDelete("zone/{id}")]
    public async Task<IActionResult> DeleteZone(int id)
    {
        try
        {
            var zone = await _db.Zones.FindAsync(id);
            if (zone == null) return NotFound();
            _db.Zones.Remove(zone);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Zone deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ============================================================
    // DTOs
    // ============================================================
    public class FarmSetupDto
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public double? Area { get; set; }
        public string? PrimaryCrop { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Address { get; set; }
        public string? OperatorEmail { get; set; }
    }

    public class GreenhouseSetupDto
    {
        public string Name { get; set; } = "";
        public double? Length { get; set; }
        public double? Width { get; set; }
        public string? ClimateControl { get; set; }
        public string? IrrigationSystem { get; set; }
    }

    public class ZoneSetupDto
    {
        public int GreenhouseId { get; set; }
        public string Name { get; set; } = "";
        public double? Area { get; set; }
        public string? Crop { get; set; }
        public string? GrowthStage { get; set; }
        public DateTime? PlantingDate { get; set; }
        public string? TargetRanges { get; set; }
    }
}