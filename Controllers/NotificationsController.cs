using HRReserveSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize]
public class NotificationsController(ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetUnread()
    {
        var currentUserName = User.Identity?.Name;
        var recruiter = !string.IsNullOrWhiteSpace(currentUserName)
            ? await context.Recruiters.AsNoTracking().FirstOrDefaultAsync(r => r.Email == currentUserName || r.FullName == currentUserName)
            : null;

        var recruiterId = recruiter?.Id.ToString();

        var query = context.AppNotifications
            .AsNoTracking()
            .Where(n => !n.IsRead && (n.RecruiterId == null || (recruiterId != null && n.RecruiterId == recruiterId)))
            .OrderByDescending(n => n.CreatedAt);

        var unreadCount = await query.CountAsync();
        var items = await query.Take(10).Select(n => new
        {
            id = n.Id,
            title = n.Title,
            message = n.Message,
            targetUrl = string.IsNullOrWhiteSpace(n.TargetUrl) ? "#" : n.TargetUrl,
            timeAgo = n.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm")
        }).ToListAsync();

        return Json(new
        {
            count = unreadCount,
            items = items
        });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var notification = await context.AppNotifications.FindAsync(id);
        if (notification is not null)
        {
            notification.IsRead = true;
            await context.SaveChangesAsync();
        }

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var currentUserName = User.Identity?.Name;
        var recruiter = !string.IsNullOrWhiteSpace(currentUserName)
            ? await context.Recruiters.AsNoTracking().FirstOrDefaultAsync(r => r.Email == currentUserName || r.FullName == currentUserName)
            : null;

        var recruiterId = recruiter?.Id.ToString();

        var unreadNotifications = await context.AppNotifications
            .Where(n => !n.IsRead && (n.RecruiterId == null || (recruiterId != null && n.RecruiterId == recruiterId)))
            .ToListAsync();

        foreach (var n in unreadNotifications)
        {
            n.IsRead = true;
        }

        await context.SaveChangesAsync();

        return Json(new { success = true, markedCount = unreadNotifications.Count });
    }
}
