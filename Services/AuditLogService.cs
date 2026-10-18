using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class AuditLogService(ApplicationDbContext context) : IAuditLogService
{
    public async Task<(List<AuditLog> Logs, int TotalItems, int TotalPages)> GetAuditLogsAsync(
        string? search,
        string? actionFilter,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default)
    {
        IQueryable<AuditLog> query = context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(l => (l.UserName != null && l.UserName.ToLower().Contains(s))
                || (l.Details != null && l.Details.ToLower().Contains(s))
                || (l.IpAddress != null && l.IpAddress.ToLower().Contains(s))
                || (l.EntityName.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            query = query.Where(l => l.Action == actionFilter);
        }

        var totalItems = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (logs, totalItems, totalPages);
    }

    public async Task<(List<EmailOutboxMessage> Messages, int TotalItems, int TotalPages, int PendingCount, int SentCount, int FailedCount)> GetOutboxMessagesAsync(
        string? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        IQueryable<EmailOutboxMessage> query = context.EmailOutboxMessages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => m.Status == status);
        }

        var totalItems = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var pendingCount = await context.EmailOutboxMessages.CountAsync(m => m.Status == "Pending", ct);
        var sentCount = await context.EmailOutboxMessages.CountAsync(m => m.Status == "Sent", ct);
        var failedCount = await context.EmailOutboxMessages.CountAsync(m => m.Status == "Failed" || m.Status == "DeadLetter", ct);

        return (messages, totalItems, totalPages, pendingCount, sentCount, failedCount);
    }

    public async Task<bool> RetryOutboxMessageAsync(int id, CancellationToken ct = default)
    {
        var message = await context.EmailOutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is not null && (message.Status == "Failed" || message.Status == "DeadLetter"))
        {
            message.Status = "Pending";
            message.RetryCount = 0;
            message.LastError = null;
            await context.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }
}
