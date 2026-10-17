using System.Text.Json;

namespace AgritechPortal.Services;

public class DailyBriefingService
{
    private readonly HttpClient _httpClient;

    public DailyBriefingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://localhost:8000/");
    }

    public async Task<string> GenerateDailyBriefingAsync(string location = "Johannesburg")
    {
        try
        {
            var response = await _httpClient.GetAsync("api/ai/ecosystem-insights/GH-01");
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                
                var liveEnv = root.GetProperty("liveEnvironment");
                double temp = liveEnv.GetProperty("externalTemperature").GetDouble();
                double humidity = liveEnv.GetProperty("externalHumidity").GetDouble();
                
                var recommendations = root.GetProperty("agronomyRecommendations");
                string recText = recommendations.GetArrayLength() > 0 
                    ? recommendations[0].GetString() ?? "Optimal climate conditions." 
                    : "Optimal climate conditions.";

                return $"🌱 **AgriAI Daily Nexus Briefing**\n" +
                       $"📍 **Location:** {location}\n" +
                       $"🌡️ **Ambient Temperature:** {temp:F1}°C\n" +
                       $"💧 **Humidity:** {humidity:F1}%\n" +
                       $"🤖 **AI Recommendation:** {recText}";
            }
        }
        catch (Exception)
        {
            // Fallback values if Python server is offline
        }

        return $"🌱 **AgriAI Daily Nexus Briefing**\n" +
               $"📍 **Location:** {location}\n" +
               $"🌡️ **Ambient Temperature:** 22.5°C\n" +
               $"💧 **Humidity:** 45%\n" +
               $"🤖 **AI Recommendation:** FastAPI engine offline. Operating on fallback telemetry profile.";
    }
}