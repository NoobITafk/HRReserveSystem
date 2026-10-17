using HRReserveSystem.Data;
using HRReserveSystem.Hubs;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[AllowAnonymous]
public class CareersController(
    ApplicationDbContext context,
    ICandidateApplicationService applicationService) : Controller
{
    private const long MaxResumeFileSize = 5 * 1024 * 1024;
    private static readonly string[] AllowedResumeExtensions = [".pdf", ".doc", ".docx"];

    [HttpGet]
    public async Task<IActionResult> Index(string? search)
    {
        IQueryable<Vacancy> query = context.Vacancies
            .AsNoTracking()
            .Where(v => !v.IsArchived && v.Status == "Open");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(v => v.Title.ToLower().Contains(searchLower)
                || v.Description.ToLower().Contains(searchLower)
                || v.Requirements.ToLower().Contains(searchLower));
        }

        var vacancies = await query
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        ViewBag.Search = search;

        return View(vacancies);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var vacancy = await context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsArchived);

        if (vacancy is null)
        {
            return NotFound();
        }

        return View(vacancy);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("CareersApplyPolicy")]
    public async Task<IActionResult> Apply(
        int vacancyId,
        string fullName,
        string email,
        string? phone,
        int? experienceYears,
        string? skills,
        string? coverLetter,
        IFormFile? resumeFile)
    {
        var vacancy = await context.Vacancies.FirstOrDefaultAsync(v => v.Id == vacancyId && !v.IsArchived);
        if (vacancy is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError(string.Empty, "ПІБ та електронна пошта обов'язкові для заповнення.");
            return View("Details", vacancy);
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (success, error, candidate, application) = await applicationService.ApplyFromCareersAsync(
            fullName,
            email,
            phone,
            null,
            null,
            null,
            coverLetter,
            clientIp,
            resumeFile!,
            vacancyId);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Не вдалося зберегти заявку.");
            return View("Details", vacancy);
        }

        return RedirectToAction(nameof(Success), new { id = application.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var application = await context.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.Vacancy)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application is null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(application);
    }
}
