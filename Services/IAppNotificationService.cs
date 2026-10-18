namespace HRReserveSystem.Services;

public interface IAppNotificationService
{
    Task<(int Count, List<NotificationItemDto> Items)> GetUnreadAsync(string? userName, CancellationToken ct = default);
    Task<(bool Success, bool Forbidden)> MarkAsReadAsync(int notificationId, string? userName, bool isAdmin, CancellationToken ct = default);
    Task<int> MarkAllAsReadAsync(string? userName, CancellationToken ct = default);
}

public record NotificationItemDto(int Id, string Title, string Message, string TargetUrl, string TimeAgo);
