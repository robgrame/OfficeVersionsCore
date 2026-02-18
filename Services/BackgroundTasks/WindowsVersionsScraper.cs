namespace OfficeVersionsCore.Services.BackgroundTasks
{
    /// <summary>
    /// Background service for scraping and updating Windows version data
    /// </summary>
    public class WindowsVersionsScraper : BackgroundService
    {
        private readonly ILogger<WindowsVersionsScraper> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        
        private readonly TimeSpan _interval;

        public WindowsVersionsScraper(
            ILogger<WindowsVersionsScraper> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;

            // Default to 60 minutes if not configured
            int intervalMinutes;
            if (!int.TryParse(_configuration["WindowsScraper:intervalMinutes"], out intervalMinutes))
            {
                intervalMinutes = 60;
            }
            _interval = TimeSpan.FromMinutes(intervalMinutes);

            _logger.LogInformation("Windows Version Scraper initialized with interval: {Interval} minutes.", 
                _interval.TotalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Windows Version Scraper service starting");

            // Initial delay to avoid startup congestion
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;
                _logger.LogInformation("Starting Windows version scraping at: {Time}", now);
                try
                {
                    await ScrapeAndUploadVersionsDataAsync(stoppingToken);
                    _logger.LogInformation("Windows version scraping completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during Windows version scraping");
                }

                // Calculate delay until next scheduled interval (aligned to second 0)
                var nextScheduledTime = CalculateNextScheduledTime(now, _interval);
                var delayUntilNext = nextScheduledTime - DateTimeOffset.Now;
                
                _logger.LogInformation("Next Windows version scraping scheduled for: {Time} (in {Delay} seconds)", 
                    nextScheduledTime, delayUntilNext.TotalSeconds);
                
                await Task.Delay(delayUntilNext, stoppingToken);
            }
        }

        /// <summary>
        /// Calculates the next scheduled execution time aligned to the start of each interval at second 0
        /// </summary>
        private DateTimeOffset CalculateNextScheduledTime(DateTimeOffset currentTime, TimeSpan interval)
        {
            // Get total minutes from a reference point (e.g., midnight)
            var minutesSinceMidnight = currentTime.TimeOfDay.TotalMinutes;
            
            // Calculate how many complete intervals have passed
            var minutesPerInterval = interval.TotalMinutes;
            var completedIntervals = Math.Floor(minutesSinceMidnight / minutesPerInterval);
            
            // Next interval starts at this time
            var nextIntervalStartMinutes = (completedIntervals + 1) * minutesPerInterval;
            
            // Create the next scheduled time at second 0 of the next interval
            var nextScheduled = new DateTimeOffset(
                currentTime.Year,
                currentTime.Month,
                currentTime.Day,
                0, 0, 0,
                currentTime.Offset
            ).AddMinutes(nextIntervalStartMinutes);
            
            // If the calculated time is before now, add another interval
            if (nextScheduled <= currentTime)
            {
                nextScheduled = nextScheduled.Add(interval);
            }
            
            return nextScheduled;
        }

        private async Task ScrapeAndUploadVersionsDataAsync(CancellationToken stoppingToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Create a scope to resolve scoped services
                using var scope = _serviceProvider.CreateScope();
                var windowsVersionsService = scope.ServiceProvider.GetRequiredService<IWindowsVersionsService>();
                
                _logger.LogInformation("Calling WindowsVersionsService.RefreshDataAsync()");
                
                var success = await windowsVersionsService.RefreshDataAsync();
                
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                
                if (success)
                {
                    _logger.LogInformation("Windows data refresh completed successfully in {ElapsedMs} ms", elapsedMs);
                    
                    // Log last update time
                    var lastUpdate = await windowsVersionsService.GetLastUpdateTimeAsync();
                    if (lastUpdate.HasValue)
                    {
                        _logger.LogInformation("Data last updated at: {LastUpdate}", lastUpdate.Value);
                    }
                }
                else
                {
                    _logger.LogWarning("Windows data refresh completed with errors in {ElapsedMs} ms", elapsedMs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Windows version scraping process");
                throw;
            }
        }

    }
}
