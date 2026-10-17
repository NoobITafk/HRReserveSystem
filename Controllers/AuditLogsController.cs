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
}
