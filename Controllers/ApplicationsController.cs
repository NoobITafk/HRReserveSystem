using HRReserveSystem.Data;
using HRReserveSystem.Hubs;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter")]
public class ApplicationsController(
    ApplicationDbContext context,
    IHubContext<KanbanHub> hubContext,
    ILogger<ApplicationsController> logger) : Controller
{
    public async Task<IActionResult> Index(string? status)
    {
        IQueryable<Application> applications = context.Applications
            .AsNoTracking()
            .Include(application => application.Candidate)
            .Include(application => application.Vacancy);

        if (!string.IsNullOrWhiteSpace(status))
        {
            applications = applications.Where(application => application.Status == status);
        }

        ViewData["StatusFilter"] = status;
        ViewData["Statuses"] = new SelectList(HrOptions.ApplicationStatuses, status);

        return View(await applications
            .OrderByDescending(application => application.AppliedAt)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var application = await context.Applications
            .AsNoTracking()
            .Include(item => item.Candidate)
            .Include(item => item.Vacancy)
            .Include(item => item.Interviews)
            .FirstOrDefaultAsync(item => item.Id == id);

        return application is null ? NotFound() : View(application);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateSelectLists();
        return View(new Application());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CandidateId,VacancyId,Status,RecruiterComment")] Application application)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSelectLists(application.CandidateId, application.VacancyId);
            return View(application);
        }

        if (await ApplicationPairExists(application.CandidateId, application.VacancyId))
        {
            ModelState.AddModelError(string.Empty, "Заявка для цього кандидата на цю вакансію вже існує.");
            await PopulateSelectLists(application.CandidateId, application.VacancyId);
            return View(application);
        }

        application.AppliedAt = DateTime.UtcNow;
        context.Add(application);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var application = await context.Applications.FindAsync(id);
        if (application is null)
        {
            return NotFound();
        }

        await PopulateSelectLists(application.CandidateId, application.VacancyId);
        return View(application);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CandidateId,VacancyId,Status,AppliedAt,RecruiterComment")] Application application)
    {
        if (id != application.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateSelectLists(application.CandidateId, application.VacancyId);
            return View(application);
        }

        if (await ApplicationPairExists(application.CandidateId, application.VacancyId, application.Id))
        {
            ModelState.AddModelError(string.Empty, "Заявка для цього кандидата на цю вакансію вже існує.");
            await PopulateSelectLists(application.CandidateId, application.VacancyId);
            return View(application);
        }

        try
        {
            context.Update(application);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await ApplicationExists(application.Id))
            {
                return NotFound();
            }

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var application = await context.Applications
            .AsNoTracking()
            .Include(item => item.Candidate)
            .Include(item => item.Vacancy)
            .FirstOrDefaultAsync(item => item.Id == id);

        return application is null ? NotFound() : View(application);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var application = await context.Applications.FindAsync(id);

        if (application is not null)
        {
            context.Applications.Remove(application);
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, string? returnStatusFilter = null)
    {
        if (!HrOptions.ApplicationStatuses.Contains(status))
        {
            TempData["ErrorMessage"] = "Обрано некоректний статус заявки.";
            return RedirectToAction(nameof(Index), new { status = returnStatusFilter });
        }

        var application = await context.Applications
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (application is null)
        {
            return NotFound();
        }

        application.Status = status;
        await context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Статус заявки для «{application.Candidate?.FullName}» змінено на «{HrOptions.ApplicationStatusLabel(status)}».";
        return RedirectToAction(nameof(Index), new { status = returnStatusFilter });
    }

    private async Task PopulateSelectLists(int? candidateId = null, int? vacancyId = null)
    {
        var candidates = await context.Candidates
            .AsNoTracking()
            .Where(candidate => !candidate.IsDeleted)
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync();
        var vacancies = await context.Vacancies
            .AsNoTracking()
            .Where(vacancy => !vacancy.IsArchived)
            .OrderBy(vacancy => vacancy.Title)
            .ToListAsync();

        ViewData["CandidateId"] = new SelectList(candidates, "Id", "FullName", candidateId);
        ViewData["VacancyId"] = new SelectList(vacancies, "Id", "Title", vacancyId);
    }

    private async Task<bool> ApplicationExists(int id)
    {
        return await context.Applications.AnyAsync(item => item.Id == id);
    }

    public async Task<IActionResult> Kanban(int? vacancyId)
    {
        IQueryable<Application> query = context.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.Vacancy)
            .Include(a => a.Interviews)
            .Include(a => a.Stage);

        if (vacancyId.HasValue && vacancyId.Value > 0)
        {
            query = query.Where(a => a.VacancyId == vacancyId.Value);
        }

        var applications = await query
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();

        var vacancies = await context.Vacancies
            .AsNoTracking()
            .Where(v => !v.IsArchived)
            .OrderBy(v => v.Title)
            .ToListAsync();

        var stages = await context.PipelineStages
            .AsNoTracking()
            .Where(s => s.VacancyId == vacancyId || s.IsDefault)
            .OrderBy(s => s.Order)
            .ToListAsync();

        ViewData["VacancyId"] = vacancyId;
        ViewData["Vacancies"] = new SelectList(vacancies, "Id", "Title", vacancyId);
        ViewData["PipelineStages"] = stages;

        return View(applications);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatusAjax([FromBody] UpdateStatusAjaxRequest request)
    {
        if (request is null || !HrOptions.ApplicationStatuses.Contains(request.Status))
        {
            return BadRequest(new { success = false, message = "Некоректний статус етапу відбору." });
        }

        var application = await context.Applications
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == request.Id);

        if (application is null)
        {
            return NotFound(new { success = false, message = "Заявку не знайдено." });
        }

        application.Status = request.Status;
        if (request.StageId.HasValue && request.StageId.Value > 0)
        {
            application.StageId = request.StageId.Value;
        }

        if (request.Status == "Offer" || request.Status == "Hired")
        {
            context.AppNotifications.Add(new AppNotification
            {
                Title = request.Status == "Hired" ? "Кандидата прийнято на роботу!" : "Кандидату виставлено офер!",
                Message = $"{application.Candidate?.FullName ?? "Кандидата"} переведено на етап «{HrOptions.ApplicationStatusLabel(request.Status)}»",
                TargetUrl = $"/Applications/Kanban?vacancyId={application.VacancyId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();

        try
        {
            var payload = new
            {
                id = application.Id,
                candidateName = application.Candidate?.FullName,
                vacancyId = application.VacancyId,
                status = application.Status,
                stageId = application.StageId,
                updatedBy = User.Identity?.Name ?? "Рекрутер"
            };

            await hubContext.Clients.Group($"vacancy-{application.VacancyId}").SendAsync("ApplicationMoved", payload);
            await hubContext.Clients.Group("vacancy-all").SendAsync("ApplicationMoved", payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast ApplicationMoved via SignalR");
        }

        return Json(new
        {
            success = true,
            id = application.Id,
            status = application.Status,
            stageId = application.StageId,
            statusLabel = HrOptions.ApplicationStatusLabel(application.Status),
            statusBadge = HrOptions.ApplicationStatusBadge(application.Status),
            candidateName = application.Candidate?.FullName,
            message = $"Статус для {application.Candidate?.FullName} оновлено на «{HrOptions.ApplicationStatusLabel(application.Status)}»"
        });
    }

    private async Task<bool> ApplicationPairExists(int candidateId, int vacancyId, int? excludedApplicationId = null)
    {
        return await context.Applications.AnyAsync(item =>
            item.CandidateId == candidateId &&
            item.VacancyId == vacancyId &&
            (!excludedApplicationId.HasValue || item.Id != excludedApplicationId.Value));
    }
}

public record UpdateStatusAjaxRequest(int Id, string Status, int? StageId = null);
