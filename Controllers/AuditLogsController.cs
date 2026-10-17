using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin")]
public class AuditLogsController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index(string? search, string? actionFilter, int page = 1)
    {
        const int pageSize = 30;
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

        var totalItems = await query.CountAsync();
        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.ActionFilter = actionFilter;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        ViewBag.TotalItems = totalItems;

        return View(logs);
    }

    public async Task<IActionResult> Outbox(string? status, int page = 1)
    {
        const int pageSize = 20;
        IQueryable<EmailOutboxMessage> query = context.EmailOutboxMessages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => m.Status == status);
        }

        var totalItems = await query.CountAsync();
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.CurrentStatus = status;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        ViewBag.TotalItems = totalItems;

        ViewBag.PendingCount = await context.EmailOutboxMessages.CountAsync(m => m.Status == "Pending");
        ViewBag.SentCount = await context.EmailOutboxMessages.CountAsync(m => m.Status == "Sent");
        ViewBag.FailedCount = await context.EmailOutboxMessages.CountAsync(m => m.Status == "Failed" || m.Status == "DeadLetter");

        return View(messages);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryOutbox(int id)
    {
        var message = await context.EmailOutboxMessages.FirstOrDefaultAsync(m => m.Id == id);
        if (message is not null && (message.Status == "Failed" || message.Status == "DeadLetter"))
        {
            message.Status = "Pending";
            message.RetryCount = 0;
            message.LastError = null;
            await context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Повідомлення #{id} повернено в чергу на відправку.";
        }

        return RedirectToAction(nameof(Outbox));
    }
}
