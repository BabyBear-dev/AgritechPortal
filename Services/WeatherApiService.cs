using System.Text.Json;

namespace AgritechPortal.Services;

public class WeatherApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<WeatherApiService> _logger;

    public WeatherApiService(HttpClient httpClient, IConfiguration config, ILogger<WeatherApiService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<WeatherSummary?> GetCurrentWeatherAsync(double lat, double lon)
    {
        var apiKey = _config["WeatherApi:Key"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Weather API key missing. Set WeatherApi:Key in appsettings.json");
            return null;
        }

        var url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={apiKey}&units=metric";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Weather API error: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new WeatherSummary
            {
                Temperature = root.GetProperty("main").GetProperty("temp").GetDecimal(),
                Humidity = root.GetProperty("main").GetProperty("humidity").GetDecimal(),
                Description = root.GetProperty("weather")[0].GetProperty("description").GetString() ?? "",
                WindSpeed = root.GetProperty("wind").GetProperty("speed").GetDecimal(),
                Rain = root.TryGetProperty("rain", out var rain) ? rain.GetProperty("1h").GetDecimal() : 0,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weather");
            return null;
        }
    }
}

public class WeatherSummary
{
    public decimal Temperature { get; set; }
    public decimal Humidity { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal WindSpeed { get; set; }
    public decimal Rain { get; set; }
    public DateTime Timestamp { get; set; }
}