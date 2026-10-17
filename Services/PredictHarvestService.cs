using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AgritechPortal.Data;
using AgritechPortal.Models;

namespace AgritechPortal.Services;

public class PredictiveHarvestService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PredictiveHarvestService> _logger;

    public PredictiveHarvestService(AppDbContext db, ILogger<PredictiveHarvestService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HarvestPrediction> GetPredictionAsync(int zoneId)
    {
        var zone = await _db.Zones
            .Include(z => z.SensorReadings)
            .FirstOrDefaultAsync(z => z.Id == zoneId);

        if (zone == null)
            throw new Exception($"Zone {zoneId} not found");

        // Group readings by type and get latest for each
        var latestReadings = zone.SensorReadings
            .GroupBy(r => r.ReadingType)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .ToDictionary(r => r.ReadingType, r => r.Value);

        decimal GetReading(string type, decimal defaultValue = 0) =>
            latestReadings.TryGetValue(type, out var val) ? val : defaultValue;

        var daysSincePlanting = zone.PlantingDate.HasValue
            ? (DateTime.UtcNow - zone.PlantingDate.Value).Days
            : 30;

        var cropCycleDays = zone.Crop?.ToLower() switch
        {
            "cucumber" => 70,
            "tomato" => 90,
            "pepper" => 80,
            "lettuce" => 45,
            _ => 80
        };

        var growthPercent = Math.Min(99, (double)daysSincePlanting / cropCycleDays * 100);

        // Soil moisture adjustment
        decimal soilMoisture = GetReading("soil_moisture");
        if (soilMoisture > 0)
        {
            double moisture = (double)soilMoisture; // explicit cast
            if (moisture < 45 || moisture > 70)
            {
                var moistureFactor = 1.0 - Math.Abs(moisture - 60) / 60;
                growthPercent = Math.Max(1, growthPercent * Math.Max(0.5, moistureFactor));
            }
        }

        var area = zone.Area ?? 100;
        var baseYieldPerSqm = zone.Crop?.ToLower() switch
        {
            "cucumber" => 6.0,
            "tomato" => 8.0,
            "pepper" => 4.0,
            _ => 5.0
        };
        var estimatedYield = Math.Round((double)area * baseYieldPerSqm * (growthPercent / 100), 0);

        var daysToHarvest = Math.Max(0, cropCycleDays - daysSincePlanting);
        var readingCount = latestReadings.Count;
        var confidence = readingCount > 0 ? Math.Min(95, 70 + readingCount * 5) : 50;
        var growthRate = Math.Round(100.0 / Math.Max(1, cropCycleDays), 1);
        var fruitDevelopment = Math.Min(100, Math.Max(0, growthPercent * 0.9));

        var riskFactors = new List<string>();

        if (soilMoisture > 0)
        {
            double moisture = (double)soilMoisture;
            if (moisture < 45 || moisture > 70)
                riskFactors.Add("💧 Water availability");
        }

        decimal pH = GetReading("pH");
        if (pH > 0)
        {
            double phVal = (double)pH;
            if (phVal < 5.5 || phVal > 7.0)
                riskFactors.Add("🧪 Soil pH");
        }

        decimal nitrogen = GetReading("nitrogen");
        decimal phosphorus = GetReading("phosphorus");
        decimal potassium = GetReading("potassium");
        if (nitrogen > 0 && (nitrogen < 50 || phosphorus < 20 || potassium < 100))
            riskFactors.Add("🧪 Nutrient levels");

        riskFactors.Add("📈 Growth rate");

        return new HarvestPrediction
        {
            ZoneId = zone.Id,
            CropName = zone.Crop ?? "Unknown",
            ZoneName = zone.Name ?? "Zone",
            GrowthPercent = Math.Round((double)growthPercent, 0),
            DaysToHarvest = daysToHarvest,
            EstimatedYieldKg = estimatedYield,
            Confidence = Math.Round((double)confidence, 0),
            GrowthRate = growthRate,
            FruitDevelopment = Math.Round((double)fruitDevelopment, 0),
            RiskFactors = riskFactors
        };
    }
}

public class HarvestPrediction
{
    public int ZoneId { get; set; }
    public string CropName { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public double GrowthPercent { get; set; }
    public int DaysToHarvest { get; set; }
    public double EstimatedYieldKg { get; set; }
    public double Confidence { get; set; }
    public double GrowthRate { get; set; }
    public double FruitDevelopment { get; set; }
    public List<string> RiskFactors { get; set; } = new();
}