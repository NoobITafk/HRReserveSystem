using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin")]
public class AuditLogsController(IAuditLogService auditLogService) : Controller
{
    public async Task<IActionResult> Index(string? search, string? actionFilter, int page = 1)
    {
        const int pageSize = 30;
        var (logs, totalItems, totalPages) = await auditLogService.GetAuditLogsAsync(search, actionFilter, page, pageSize);

        ViewBag.Search = search;
        ViewBag.ActionFilter = actionFilter;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

        return View(logs);
    }

    public async Task<IActionResult> Outbox(string? status, int page = 1)
    {
        const int pageSize = 20;
        var (messages, totalItems, totalPages, pendingCount, sentCount, failedCount) =
            await auditLogService.GetOutboxMessagesAsync(status, page, pageSize);

        ViewBag.CurrentStatus = status;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        ViewBag.PendingCount = pendingCount;
        ViewBag.SentCount = sentCount;
        ViewBag.FailedCount = failedCount;

        return View(messages);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryOutbox(int id)
    {
        var retried = await auditLogService.RetryOutboxMessageAsync(id);
        if (retried)
        {
            TempData["SuccessMessage"] = $"Повідомлення #{id} повернено в чергу на відправку.";
        }

        return RedirectToAction(nameof(Outbox));
    }
}
