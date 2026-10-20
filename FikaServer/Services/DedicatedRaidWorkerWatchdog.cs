using FikaServer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FikaServer.Services;

/// <summary>Periodically reaps crashed workers and stops workers that lost contact.</summary>
public sealed class DedicatedRaidWorkerWatchdog(
    DedicatedRaidWorkerSupervisor supervisor,
    ILogger<DedicatedRaidWorkerWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var exited = supervisor.ReapExitedWorkers();
                    var unresponsive = await supervisor.StopUnresponsiveWorkersAsync(
                        TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(5));
                    if (exited > 0 || unresponsive > 0)
                        logger.LogWarning(
                            "Dedicated workers cleaned up: {Exited} exited, {Unresponsive} unresponsive.",
                            exited, unresponsive);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Dedicated worker watchdog iteration failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
