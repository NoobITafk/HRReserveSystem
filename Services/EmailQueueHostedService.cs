using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class EmailQueueHostedService(
    IEmailBackgroundQueue emailQueue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<EmailQueueHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Durable Transactional Email Outbox Hosted Service started.");

        // Drain any messages that remained uncompleted before startup
        await ProcessOutboxBatchAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    var workItem = await emailQueue.DequeueAsync(cts.Token);
                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    await workItem(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // 5-second polling tick to check DB outbox
                }

                // Check and process database outbox messages
                await ProcessOutboxBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception occurred in EmailQueueHostedService.");
            }
        }

        logger.LogInformation("Durable Transactional Email Outbox Hosted Service stopped.");
    }

    private async Task ProcessOutboxBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

            var pendingMessages = await db.EmailOutboxMessages
                .Where(m => m.Status == "Pending" || (m.Status == "Failed" && m.RetryCount < 3))
                .OrderBy(m => m.CreatedAt)
                .Take(20)
                .ToListAsync(cancellationToken);

            if (pendingMessages.Count == 0)
            {
                return;
            }

            foreach (var message in pendingMessages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                message.Status = "Processing";
                await db.SaveChangesAsync(cancellationToken);

                try
                {
                    await emailService.SendCandidateMessageAsync(
                        message.RecipientEmail,
                        message.RecipientName ?? "",
                        message.Subject,
                        message.Body);

                    message.Status = "Sent";
                    message.ProcessedAt = DateTime.UtcNow;
                    message.LastError = null;
                    logger.LogInformation("Outbox email {MessageId} to {Recipient} delivered successfully", message.Id, message.RecipientEmail);
                }
                catch (Exception ex)
                {
                    message.RetryCount++;
                    message.Status = message.RetryCount >= 3 ? "DeadLetter" : "Failed";
                    message.LastError = ex.Message;
                    logger.LogWarning(ex, "Outbox email {MessageId} delivery failed (attempt {RetryCount})", message.Id, message.RetryCount);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to process outbox batch");
        }
    }
}
