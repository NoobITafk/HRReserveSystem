using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers;

[Authorize]
public class NotificationsController(IAppNotificationService notificationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetUnread()
    {
        var (count, items) = await notificationService.GetUnreadAsync(User.Identity?.Name);
        return Json(new { count, items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var isAdmin = User.IsInRole("Admin");
        var (success, forbidden) = await notificationService.MarkAsReadAsync(id, User.Identity?.Name, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        return Json(new { success });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var markedCount = await notificationService.MarkAllAsReadAsync(User.Identity?.Name);
        return Json(new { success = true, markedCount });
    }
}
