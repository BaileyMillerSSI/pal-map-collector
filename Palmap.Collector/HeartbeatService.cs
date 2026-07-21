using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Palmap.Collector;

internal sealed class HeartbeatService(CollectorOptions options, TimeProvider timeProvider, ILogger<HeartbeatService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await File.WriteAllTextAsync(
                    options.HeartbeatPath,
                    timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    stoppingToken);
            }
            catch (IOException exception)
            {
                logger.LogWarning("Unable to update the process heartbeat ({ExceptionType}).", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    public static bool IsHealthy(string path)
    {
        try
        {
            var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
            return age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(1);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
