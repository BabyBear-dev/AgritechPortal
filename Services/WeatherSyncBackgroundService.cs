using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgritechPortal.Services
{
    public class WeatherSyncBackgroundService : BackgroundService
    {
        private readonly ILogger<WeatherSyncBackgroundService> _logger;
        private readonly IServiceProvider _services;

        public WeatherSyncBackgroundService(ILogger<WeatherSyncBackgroundService> logger, IServiceProvider services)
        {
            _logger = logger;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var weatherService = scope.ServiceProvider.GetRequiredService<WeatherSyncService>();
                    await weatherService.SyncWeatherAsync("GH-01");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background sync failed");
                }

                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
    }
}