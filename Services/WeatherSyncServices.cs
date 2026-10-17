using Microsoft.Extensions.Logging;

namespace AgritechPortal.Services
{
    public class WeatherSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WeatherSyncService> _logger;

        public WeatherSyncService(HttpClient httpClient, ILogger<WeatherSyncService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> SyncWeatherAsync(string greenhouseId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://localhost:8000/api/ai/ecosystem-insights/{greenhouseId}");
                var result = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully synced real-time climate/pest insights: {result}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weather sync failed");
                return "{}";
            }
        }
    }
}