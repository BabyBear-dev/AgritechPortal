using Microsoft.EntityFrameworkCore;
using AgritechPortal.Data;
using AgritechPortal.Models;

namespace AgritechPortal.Services;

public class DataIngestionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataIngestionService> _logger;

    public DataIngestionService(AppDbContext db, ILogger<DataIngestionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Register a new sensor device
    /// </summary>
    public async Task<SensorDevice> RegisterDeviceAsync(int zoneId, string name, string deviceType, string? serialNumber = null)
    {
        var device = new SensorDevice
        {
            ZoneId = zoneId,
            Name = name,
            DeviceType = deviceType,
            SerialNumber = serialNumber,
            Status = "online",
            LastSeen = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.SensorDevices.Add(device);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Registered device {Name} (ID: {Id}) for zone {ZoneId}", name, device.Id, zoneId);
        return device;
    }

    /// <summary>
    /// Ingest a sensor reading (validated)
    /// </summary>
    public async Task<SensorReading> IngestReadingAsync(int deviceId, string readingType, decimal value, string? unit = null)
    {
        var device = await _db.SensorDevices.FindAsync(deviceId);
        if (device == null)
            throw new ArgumentException($"Device {deviceId} not found");

        if (!IsValidReading(readingType, value))
        {
            _logger.LogWarning("Invalid {Type} reading {Value} from device {DeviceId}", readingType, value, deviceId);
            throw new ArgumentException($"Invalid {readingType} reading: {value}");
        }

        // Update device status
        device.LastSeen = DateTime.UtcNow;
        device.Status = "online";
        device.UpdatedAt = DateTime.UtcNow;

        var reading = new SensorReading
        {
            DeviceId = deviceId,
            ReadingType = readingType,
            Value = value,
            Unit = unit,
            Timestamp = DateTime.UtcNow,
            IsValid = true,
            Source = "sensor"
        };

        _db.SensorReadings.Add(reading);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Ingested {Type}={Value}{Unit} from device {DeviceId}", readingType, value, unit, deviceId);
        return reading;
    }

    /// <summary>
    /// Get the latest reading for each type in a zone (for AI context)
    /// </summary>
    public async Task<Dictionary<string, (decimal Value, string? Unit, DateTime Timestamp)>> GetLatestTelemetryAsync(int zoneId)
    {
        var readings = await _db.SensorReadings
            .Include(r => r.Device)
            .Where(r => r.Device != null && r.Device.ZoneId == zoneId && r.IsValid)
            .OrderByDescending(r => r.Timestamp)
            .ToListAsync();

        return readings
            .GroupBy(r => r.ReadingType)
            .Select(g => g.First())
            .ToDictionary(
                r => r.ReadingType,
                r => (r.Value, r.Unit, r.Timestamp)
            );
    }

    /// <summary>
    /// Get the full status for a zone (all devices and latest readings)
    /// </summary>
    public async Task<object> GetZoneStatusAsync(int zoneId)
    {
        var devices = await _db.SensorDevices
            .Where(d => d.ZoneId == zoneId)
            .ToListAsync();

        var readings = await _db.SensorReadings
            .Include(r => r.Device)
            .Where(r => r.Device != null && r.Device.ZoneId == zoneId && r.IsValid)
            .OrderByDescending(r => r.Timestamp)
            .ToListAsync();

        var latestReadings = readings
            .GroupBy(r => r.ReadingType)
            .Select(g => g.First())
            .ToDictionary(
                r => r.ReadingType,
                r => new { r.Value, r.Unit, r.Timestamp }
            );

        return new
        {
            ZoneId = zoneId,
            DeviceCount = devices.Count,
            OnlineDevices = devices.Count(d => d.Status == "online"),
            OfflineDevices = devices.Count(d => d.Status == "offline"),
            LatestReadings = latestReadings,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Mark devices as offline if they haven't reported in X minutes
    /// </summary>
    public async Task MarkStaleDevicesAsync(int timeoutMinutes = 15)
    {
        var stale = await _db.SensorDevices
            .Where(d => d.LastSeen < DateTime.UtcNow.AddMinutes(-timeoutMinutes) && d.Status == "online")
            .ToListAsync();

        foreach (var d in stale)
        {
            d.Status = "offline";
            d.UpdatedAt = DateTime.UtcNow;
            _logger.LogWarning("Device {Id} ({Name}) marked offline - last seen at {LastSeen}", d.Id, d.Name, d.LastSeen);
        }

        if (stale.Any())
            await _db.SaveChangesAsync();
    }

    private bool IsValidReading(string type, decimal value)
    {
        return type switch
        {
            "temperature" => value >= -10 && value <= 50,
            "humidity" => value >= 0 && value <= 100,
            "pH" => value >= 0 && value <= 14,
            "EC" => value >= 0 && value <= 10,
            "CO2" => value >= 0 && value <= 5000,
            "light" => value >= 0 && value <= 100000,
            "soil_moisture" => value >= 0 && value <= 100,
            _ => true
        };
    }
}