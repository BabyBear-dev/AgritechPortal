using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgritechPortal.Data;
using AgritechPortal.Models;

namespace AgritechPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecommendationController : ControllerBase
{
    private readonly AppDbContext _db;

    public RecommendationController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var pending = await _db.Recommendations
            .Include(r => r.Zone)
            .Where(r => r.Status == "pending")
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(pending);
    }

    [HttpPost("approve/{id}")]
    public async Task<IActionResult> Approve(int id)
    {
        var rec = await _db.Recommendations.FindAsync(id);
        if (rec == null) return NotFound();
        rec.Status = "approved";
        await _db.SaveChangesAsync();
        return Ok(rec);
    }

    [HttpPost("reject/{id}")]
    public async Task<IActionResult> Reject(int id)
    {
        var rec = await _db.Recommendations.FindAsync(id);
        if (rec == null) return NotFound();
        rec.Status = "rejected";
        await _db.SaveChangesAsync();
        return Ok(rec);
    }

    [HttpGet("memory/{zoneId}")]
    public async Task<IActionResult> GetMemory(int zoneId)
    {
        var memories = await _db.FarmMemories
            .Where(m => m.ZoneId == zoneId)
            .OrderByDescending(m => m.Date)
            .Take(10)
            .Select(m => new
            {
                Timestamp = m.Date,
                UserQuery = m.Observation,
                AISummary = m.Decision
            })
            .ToListAsync();
        return Ok(memories);
    }
}