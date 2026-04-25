using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PatinaBlazor.Hubs;

namespace PatinaBlazor.Services;

/// <summary>
/// Sends periodic Ping messages to all registered bot connections and releases
/// any that have not responded within the stale threshold.
/// Intervals are configured via IrcApi:PingIntervalSeconds and IrcApi:StaleThresholdSeconds.
/// </summary>
public class IrcBotHeartbeatService(
    IrcBotService botService,
    IHubContext<IrcBotHub> hubContext,
    IOptions<IrcApiSettings> settings,
    ILogger<IrcBotHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pingInterval = TimeSpan.FromSeconds(settings.Value.PingIntervalSeconds);
            var staleThreshold = TimeSpan.FromSeconds(settings.Value.StaleThresholdSeconds);

            await Task.Delay(pingInterval, stoppingToken);

            var stale = botService.GetStaleConnectionIds(staleThreshold);
            foreach (var id in stale)
            {
                logger.LogWarning("Releasing stale bot connection {ConnectionId}", id);
                botService.Release(id);
            }

            var connections = botService.GetAllConnectionIds();
            foreach (var id in connections)
            {
                await hubContext.Clients.Client(id).SendAsync("Ping", cancellationToken: stoppingToken);
            }
        }
    }
}
