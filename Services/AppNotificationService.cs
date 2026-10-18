using HRReserveSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class AppNotificationService(ApplicationDbContext context) : IAppNotificationService
{
    public async Task<(int Count, List<NotificationItemDto> Items)> GetUnreadAsync(string? userName, CancellationToken ct = default)
    {
        var recruiterId = await GetRecruiterIdAsync(userName, ct);

        var query = context.AppNotifications
            .AsNoTracking()
            .Where(n => !n.IsRead && (n.RecruiterId == null || (recruiterId != null && n.RecruiterId == recruiterId)))
            .OrderByDescending(n => n.CreatedAt);

        var unreadCount = await query.CountAsync(ct);
        var items = await query.Take(10).Select(n => new NotificationItemDto(
            n.Id,
            n.Title,
            n.Message,
            string.IsNullOrWhiteSpace(n.TargetUrl) ? "#" : n.TargetUrl,
            n.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm")
        )).ToListAsync(ct);

        return (unreadCount, items);
    }

    public async Task<(bool Success, bool Forbidden)> MarkAsReadAsync(int notificationId, string? userName, bool isAdmin, CancellationToken ct = default)
    {
        var notification = await context.AppNotifications.FirstOrDefaultAsync(n => n.Id == notificationId, ct);
        if (notification is null)
        {
            return (false, false);
        }

        var recruiterId = await GetRecruiterIdAsync(userName, ct);

        // IDOR / BOLA authorization check:
        // Notification can be marked as read only if it's a global notification (RecruiterId == null),
        // or belongs to the current user, or user is Admin.
        var isOwner = notification.RecruiterId == null || (recruiterId != null && notification.RecruiterId == recruiterId);
        if (!isOwner && !isAdmin)
        {
            return (false, true); // Forbidden
        }

        notification.IsRead = true;
        await context.SaveChangesAsync(ct);
        return (true, false);
    }

    public async Task<int> MarkAllAsReadAsync(string? userName, CancellationToken ct = default)
    {
        var recruiterId = await GetRecruiterIdAsync(userName, ct);

        var unreadNotifications = await context.AppNotifications
            .Where(n => !n.IsRead && (n.RecruiterId == null || (recruiterId != null && n.RecruiterId == recruiterId)))
            .ToListAsync(ct);

        foreach (var n in unreadNotifications)
        {
            n.IsRead = true;
        }

        await context.SaveChangesAsync(ct);
        return unreadNotifications.Count;
    }

    private async Task<string?> GetRecruiterIdAsync(string? userName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var recruiter = await context.Recruiters
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Email == userName || r.FullName == userName || r.Login == userName, ct);

        return recruiter?.Id.ToString();
    }
}
