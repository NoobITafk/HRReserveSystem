using HRReserveSystem.Data;
using HRReserveSystem.Hubs;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[AllowAnonymous]
public class CareersController(
    ApplicationDbContext context,
    IWebHostEnvironment environment,
    ResumeIntelligenceService resumeIntelligence,
    IHubContext<KanbanHub> hubContext,
    ILogger<CareersController> logger) : Controller
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

        var cleanEmail = email.Trim().ToLowerInvariant();
        var candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Email.ToLower() == cleanEmail && !c.IsDeleted);

        if (candidate is null)
        {
            candidate = new Candidate
            {
                FullName = fullName.Trim(),
                Email = cleanEmail,
                Phone = phone?.Trim(),
                ExperienceYears = experienceYears ?? 0,
                Skills = string.IsNullOrWhiteSpace(skills) ? "Заявка через кар'єрний портал" : skills.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            context.Candidates.Add(candidate);
            await context.SaveChangesAsync();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(phone)) candidate.Phone = phone.Trim();
            if (experienceYears.HasValue && experienceYears.Value > 0) candidate.ExperienceYears = experienceYears.Value;
            if (!string.IsNullOrWhiteSpace(skills)) candidate.Skills = string.IsNullOrWhiteSpace(candidate.Skills) ? skills.Trim() : $"{candidate.Skills}, {skills.Trim()}";
            await context.SaveChangesAsync();
        }

        // Handle Resume Upload
        if (resumeFile is not null && resumeFile.Length > 0)
        {
            var ext = Path.GetExtension(resumeFile.FileName).ToLowerInvariant();
            if (AllowedResumeExtensions.Contains(ext) && resumeFile.Length <= MaxResumeFileSize)
            {
                var uniqueName = $"{Guid.NewGuid():N}{ext}";
                var targetDir = Path.Combine(environment.WebRootPath, "uploads", "resumes");
                Directory.CreateDirectory(targetDir);
                var fullPath = Path.Combine(targetDir, uniqueName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await resumeFile.CopyToAsync(stream);
                }

                candidate.ResumeFilePath = $"/uploads/resumes/{uniqueName}";

                var parsed = resumeIntelligence.ParseResumeFile(fullPath);
                if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
                {
                    candidate.ResumeSummary = parsed.Summary;
                }

                await context.SaveChangesAsync();
            }
        }

        // Check if application exists
        var existingApp = await context.Applications.FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.VacancyId == vacancyId);
        if (existingApp is null)
        {
            var defaultStage = await context.PipelineStages
                .AsNoTracking()
                .Where(s => s.Code == "New" && (s.VacancyId == vacancyId || s.IsDefault))
                .OrderBy(s => s.Order)
                .FirstOrDefaultAsync();

            var application = new Application
            {
                CandidateId = candidate.Id,
                VacancyId = vacancyId,
                Status = "New",
                StageId = defaultStage?.Id,
                AppliedAt = DateTime.UtcNow,
                RecruiterComment = string.IsNullOrWhiteSpace(coverLetter) ? "Відгук через публічний кар'єрний портал" : $"Супровідний лист: {coverLetter.Trim()}"
            };

            context.Applications.Add(application);

            // In-app notification for team
            context.AppNotifications.Add(new AppNotification
            {
                Title = $"Новий відгук: {vacancy.Title}",
                Message = $"Кандидат: {candidate.FullName} ({candidate.Email})",
                TargetUrl = $"/Applications/Kanban?vacancyId={vacancyId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync();

            // Broadcast via SignalR to Recruiters viewing the board
            try
            {
                await hubContext.Clients.Group($"vacancy-{vacancyId}").SendAsync("ApplicationCreated", new
                {
                    id = application.Id,
                    candidateName = candidate.FullName,
                    vacancyTitle = vacancy.Title,
                    status = "New"
                });
                await hubContext.Clients.Group("vacancy-all").SendAsync("ApplicationCreated", new
                {
                    id = application.Id,
                    candidateName = candidate.FullName,
                    vacancyTitle = vacancy.Title,
                    status = "New"
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to broadcast ApplicationCreated via SignalR");
            }

            return RedirectToAction(nameof(Success), new { id = application.Id });
        }

        return RedirectToAction(nameof(Success), new { id = existingApp.Id });
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
