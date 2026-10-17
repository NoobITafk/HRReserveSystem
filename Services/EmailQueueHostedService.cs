namespace HRReserveSystem.Services;

public class EmailQueueHostedService(
    IEmailBackgroundQueue emailQueue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<EmailQueueHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background Email Queue Hosted Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await emailQueue.DequeueAsync(stoppingToken);
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown requested
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception occurred executing background email work item.");
            }
        }

        logger.LogInformation("Background Email Queue Hosted Service stopped.");
    }
}
