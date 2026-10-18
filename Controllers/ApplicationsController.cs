using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter")]
public class ApplicationsController(ICandidateApplicationService applicationService) : Controller
{
    public async Task<IActionResult> Index(string? status, int page = 1)
    {
        var (pagedApplications, totalItems, totalPages) = await applicationService.GetApplicationsAsync(status, page, 25);

        ViewData["StatusFilter"] = status;
        ViewData["Statuses"] = new SelectList(HrOptions.ApplicationStatuses, status);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

        return View(pagedApplications);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var application = await applicationService.GetApplicationDetailsAsync(id.Value);
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

        if (await applicationService.ApplicationPairExistsAsync(application.CandidateId, application.VacancyId))
        {
            ModelState.AddModelError(string.Empty, "Заявка для цього кандидата на цю вакансію вже існує.");
            await PopulateSelectLists(application.CandidateId, application.VacancyId);
            return View(application);
        }

        await applicationService.CreateApplicationAsync(application);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var application = await applicationService.GetApplicationForEditAsync(id.Value);
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

        if (await applicationService.ApplicationPairExistsAsync(application.CandidateId, application.VacancyId, application.Id))
        {
            ModelState.AddModelError(string.Empty, "Заявка для цього кандидата на цю вакансію вже існує.");
            await PopulateSelectLists(application.CandidateId, application.VacancyId);
            return View(application);
        }

        var (success, error) = await applicationService.UpdateApplicationAsync(application);
        if (!success)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var application = await applicationService.GetApplicationDetailsAsync(id.Value);
        return application is null ? NotFound() : View(application);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await applicationService.DeleteApplicationAsync(id);
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

        var (success, candidateName, error) = await applicationService.UpdateApplicationStatusAsync(id, status);
        if (!success)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = $"Статус заявки для «{candidateName}» змінено на «{HrOptions.ApplicationStatusLabel(status)}».";
        return RedirectToAction(nameof(Index), new { status = returnStatusFilter });
    }

    private async Task PopulateSelectLists(int? candidateId = null, int? vacancyId = null)
    {
        var (candidates, vacancies) = await applicationService.GetApplicationSelectListsDataAsync();
        ViewData["CandidateId"] = new SelectList(candidates, "Id", "FullName", candidateId);
        ViewData["VacancyId"] = new SelectList(vacancies, "Id", "Title", vacancyId);
    }

    public async Task<IActionResult> Kanban(int? vacancyId)
    {
        var (vacancies, stages, applications) = await applicationService.GetKanbanDataAsync(vacancyId);

        ViewData["VacancyId"] = vacancyId;
        ViewData["Vacancies"] = new SelectList(vacancies, "Id", "Title", vacancyId);
        ViewData["PipelineStages"] = stages;

        return View(applications);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatusAjax([FromBody] UpdateStatusAjaxRequest request)
    {
        if (request is null || !HrOptions.ApplicationStatuses.Contains(request.Status))
        {
            return BadRequest(new { success = false, message = "Некоректний статус етапу відбору." });
        }

        var (success, error) = await applicationService.MoveApplicationStageAsync(request.Id, request.Status, request.StageId);
        if (!success)
        {
            return NotFound(new { success = false, message = error ?? "Заявку не знайдено." });
        }

        var application = await applicationService.GetApplicationWithCandidateAsync(request.Id);

        return Json(new
        {
            success = true,
            id = request.Id,
            status = request.Status,
            stageId = request.StageId,
            statusLabel = HrOptions.ApplicationStatusLabel(request.Status),
            statusBadge = HrOptions.ApplicationStatusBadge(request.Status),
            candidateName = application?.Candidate?.FullName,
            message = $"Статус для {application?.Candidate?.FullName} оновлено на «{HrOptions.ApplicationStatusLabel(request.Status)}»"
        });
    }
}

public record UpdateStatusAjaxRequest(int Id, string Status, int? StageId = null);
