using System.Security.Claims;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Interviewer")]
public class InterviewFeedbacksController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index()
    {
        var feedbacks = context.InterviewFeedbacks
            .AsNoTracking()
            .Include(feedback => feedback.Recruiter)
            .Include(feedback => feedback.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Candidate)
            .Include(feedback => feedback.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Vacancy);

        return View(await feedbacks
            .OrderByDescending(feedback => feedback.CreatedAt)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var feedback = await context.InterviewFeedbacks
            .AsNoTracking()
            .Include(item => item.Recruiter)
            .Include(item => item.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Candidate)
            .Include(item => item.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Vacancy)
            .FirstOrDefaultAsync(item => item.Id == id);

        return feedback is null ? NotFound() : View(feedback);
    }

    public async Task<IActionResult> Create(int? interviewId)
    {
        var currentRecruiterId = await GetCurrentRecruiterId();
        await PopulateInterviewsSelectList(interviewId);
        await PopulateRecruitersSelectList(currentRecruiterId);

        return View(new InterviewFeedback
        {
            InterviewId = interviewId ?? 0,
            RecruiterId = currentRecruiterId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("InterviewId,RecruiterId,Comment,Score,Recommendation")] InterviewFeedback feedback)
    {
        if (User.IsInRole("Interviewer"))
        {
            feedback.RecruiterId = await GetCurrentRecruiterId();
        }

        if (!ModelState.IsValid)
        {
            await PopulateInterviewsSelectList(feedback.InterviewId);
            await PopulateRecruitersSelectList(feedback.RecruiterId);
            return View(feedback);
        }

        feedback.CreatedAt = DateTime.UtcNow;
        context.Add(feedback);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Interviewer")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var feedback = await context.InterviewFeedbacks.FindAsync(id);
        if (feedback is null)
        {
            return NotFound();
        }

        await PopulateInterviewsSelectList(feedback.InterviewId);
        await PopulateRecruitersSelectList(feedback.RecruiterId);
        return View(feedback);
    }

    [Authorize(Roles = "Admin,Interviewer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,InterviewId,RecruiterId,Comment,Score,Recommendation,CreatedAt")] InterviewFeedback feedback)
    {
        if (User.IsInRole("Interviewer"))
        {
            feedback.RecruiterId = await GetCurrentRecruiterId();
        }

        if (id != feedback.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateInterviewsSelectList(feedback.InterviewId);
            await PopulateRecruitersSelectList(feedback.RecruiterId);
            return View(feedback);
        }

        try
        {
            context.Update(feedback);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await FeedbackExists(feedback.Id))
            {
                return NotFound();
            }

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Interviewer")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var feedback = await context.InterviewFeedbacks
            .AsNoTracking()
            .Include(item => item.Recruiter)
            .Include(item => item.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Candidate)
            .Include(item => item.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Vacancy)
            .FirstOrDefaultAsync(item => item.Id == id);

        return feedback is null ? NotFound() : View(feedback);
    }

    [Authorize(Roles = "Admin,Interviewer")]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var feedback = await context.InterviewFeedbacks.FindAsync(id);

        if (feedback is not null)
        {
            context.InterviewFeedbacks.Remove(feedback);
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateInterviewsSelectList(int? selectedInterviewId = null)
    {
        var interviews = await context.Interviews
            .AsNoTracking()
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Vacancy)
            .Where(interview => interview.Application != null)
            .Where(interview => interview.Application!.Candidate != null && !interview.Application.Candidate.IsDeleted)
            .Where(interview => interview.Application!.Vacancy != null && !interview.Application.Vacancy.IsArchived)
            .OrderByDescending(interview => interview.InterviewDate)
            .ToListAsync();

        var items = interviews.Select(interview => new
        {
            interview.Id,
            Label = $"{interview.Application?.Candidate?.FullName} - {interview.Application?.Vacancy?.Title} ({interview.InterviewDate:g})"
        });

        ViewData["InterviewId"] = new SelectList(items, "Id", "Label", selectedInterviewId);
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

    private async Task<bool> FeedbackExists(int id)
    {
        return await context.InterviewFeedbacks.AnyAsync(item => item.Id == id);
    }
}
