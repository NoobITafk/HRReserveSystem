using System.Security.Claims;
using System.Text;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Interviewer")]
public class InterviewsController(
    IInterviewService interviewService,
    IGoogleCalendarSyncService googleCalendarSync,
    ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index(string? interviewType, string? result, int page = 1)
    {
        var (interviews, totalItems, totalPages) = await interviewService.GetInterviewsAsync(interviewType, result, page, 25);

        ViewData["InterviewTypeFilter"] = interviewType;
        ViewData["ResultFilter"] = result;
        ViewData["InterviewTypes"] = new SelectList(HrOptions.InterviewTypes, interviewType);
        ViewData["Results"] = new SelectList(HrOptions.InterviewResults, result);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

        return View(interviews);
    }

    public async Task<IActionResult> Calendar()
    {
        var interviews = await interviewService.GetCalendarInterviewsAsync();
        return View(interviews);
    }

    public async Task<IActionResult> ExportIcs(int? id = null)
    {
        if (id.HasValue)
        {
            var interview = await interviewService.GetInterviewDetailsAsync(id.Value);
            if (interview is null) return NotFound();

            var content = interviewService.GenerateIcsContent(interview);
            return File(Encoding.UTF8.GetBytes(content), "text/calendar; charset=utf-8", $"interview-{id.Value}.ics");
        }

        var allInterviews = await interviewService.GetCalendarInterviewsAsync();
        var sb = new StringBuilder();
        foreach (var interview in allInterviews)
        {
            sb.Append(interviewService.GenerateIcsContent(interview));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/calendar; charset=utf-8", "interviews-calendar.ics");
    }

    public async Task<IActionResult> GoogleCalendar(int id)
    {
        var interview = await interviewService.GetInterviewDetailsAsync(id);
        if (interview is null)
        {
            return NotFound();
        }

        return Redirect(interviewService.GetGoogleCalendarUrl(interview));
    }

    [Authorize(Roles = "Admin,Recruiter")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncGoogleCloud(int id)
    {
        var interview = await interviewService.GetInterviewDetailsAsync(id);
        if (interview is null) return NotFound();

        var (success, eventId, error) = await googleCalendarSync.CreateEventAsync(interview);
        if (success)
        {
            TempData["SuccessMessage"] = $"Співбесіду #{id} успішно синхронізовано з Google Calendar (Event ID: {eventId}).";
        }
        else
        {
            TempData["ErrorMessage"] = $"Не вдалося синхронізувати з Google Calendar: {error}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var interview = await interviewService.GetInterviewDetailsAsync(id.Value);
        return interview is null ? NotFound() : View(interview);
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Create()
    {
        await PopulateApplicationsSelectList();
        await PopulateRecruitersSelectList(await GetCurrentRecruiterId());
        return View(new Interview { InterviewDate = DateTime.Now, RecruiterId = await GetCurrentRecruiterId() });
    }

    [Authorize(Roles = "Admin,Recruiter")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("ApplicationId,RecruiterId,InterviewDate,InterviewType,Result,Notes")] Interview interview)
    {
        if (!ModelState.IsValid)
        {
            await PopulateApplicationsSelectList(interview.ApplicationId);
            await PopulateRecruitersSelectList(interview.RecruiterId);
            return View(interview);
        }

        await interviewService.ScheduleInterviewAsync(interview);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var interview = await interviewService.GetInterviewDetailsAsync(id.Value);
        if (interview is null)
        {
            return NotFound();
        }

        await PopulateApplicationsSelectList(interview.ApplicationId);
        await PopulateRecruitersSelectList(interview.RecruiterId);
        return View(interview);
    }

    [Authorize(Roles = "Admin,Recruiter")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,ApplicationId,RecruiterId,InterviewDate,InterviewType,Result,Notes")] Interview interview)
    {
        if (id != interview.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateApplicationsSelectList(interview.ApplicationId);
            await PopulateRecruitersSelectList(interview.RecruiterId);
            return View(interview);
        }

        var (success, error) = await interviewService.UpdateInterviewAsync(interview);
        if (!success)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var interview = await interviewService.GetInterviewDetailsAsync(id.Value);
        return interview is null ? NotFound() : View(interview);
    }

    [Authorize(Roles = "Admin,Recruiter")]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await interviewService.DeleteInterviewAsync(id);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateApplicationsSelectList(int? selectedApplicationId = null)
    {
        var applications = await context.Applications
            .AsNoTracking()
            .Include(application => application.Candidate)
            .Include(application => application.Vacancy)
            .Where(application => application.Candidate != null && !application.Candidate.IsDeleted)
            .Where(application => application.Vacancy != null && !application.Vacancy.IsArchived)
            .OrderByDescending(application => application.AppliedAt)
            .ToListAsync();

        var items = applications.Select(application => new
        {
            application.Id,
            Label = $"{application.Candidate?.FullName} - {application.Vacancy?.Title}"
        });

        ViewData["ApplicationId"] = new SelectList(items, "Id", "Label", selectedApplicationId);
    }

    private async Task PopulateRecruitersSelectList(int? selectedRecruiterId = null)
    {
        var recruiters = await context.Recruiters
            .AsNoTracking()
            .OrderBy(recruiter => recruiter.FullName)
            .ToListAsync();

        ViewData["RecruiterId"] = new SelectList(recruiters, "Id", "FullName", selectedRecruiterId);
    }

    private async Task<int?> GetCurrentRecruiterId()
    {
        var idValue = User.FindFirstValue(IdentityRecruiterSyncService.RecruiterIdClaim)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idValue, out var id) && await context.Recruiters.AnyAsync(recruiter => recruiter.Id == id)
            ? id
            : null;
    }
}
