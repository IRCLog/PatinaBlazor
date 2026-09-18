namespace PatinaBlazor.Services
{
    // Runs IStorageBillingService.RunDueBillingAsync once a day, timed to UTC midnight
    // (this app's established convention is UTC everywhere - see the Serilog checkpoint
    // entry's "40 DateTime.UtcNow call sites, zero DateTime.Now" grep - so the billing
    // calendar is anchored the same way, not to any particular local business timezone).
    // Computes the delay until the next occurrence and Task.Delays straight to it, rather
    // than looping on a fixed interval (IrcBotHeartbeatService's simpler shape) - a fixed
    // interval would drift over time and wouldn't reliably land once per calendar day.
    public class StorageBillingHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StorageBillingHostedService> _logger;

        public StorageBillingHostedService(IServiceScopeFactory scopeFactory, ILogger<StorageBillingHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var nextRunUtc = now.Date.AddDays(1);
                var delay = nextRunUtc - now;

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var billingService = scope.ServiceProvider.GetRequiredService<IStorageBillingService>();
                    await billingService.RunDueBillingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nightly storage billing run threw before completing.");
                }
            }
        }
    }
}
