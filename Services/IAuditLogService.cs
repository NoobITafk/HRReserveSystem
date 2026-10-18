using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface IAuditLogService
{
    Task<(List<AuditLog> Logs, int TotalItems, int TotalPages)> GetAuditLogsAsync(
        string? search,
        string? actionFilter,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default);

    Task<(List<EmailOutboxMessage> Messages, int TotalItems, int TotalPages, int PendingCount, int SentCount, int FailedCount)> GetOutboxMessagesAsync(
        string? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<bool> RetryOutboxMessageAsync(int id, CancellationToken ct = default);
}
