using System.Security.Claims;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Interviewer")]
public class InterviewFeedbacksController(IInterviewFeedbackService feedbackService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var feedbacks = await feedbackService.GetFeedbacksAsync();
        return View(feedbacks);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var feedback = await feedbackService.GetFeedbackDetailsAsync(id.Value);
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
        var currentRecruiterId = await GetCurrentRecruiterId();
        var isInterviewer = User.IsInRole("Interviewer");

        if (isInterviewer)
        {
            feedback.RecruiterId = currentRecruiterId;
        }

        if (!ModelState.IsValid)
        {
            await PopulateInterviewsSelectList(feedback.InterviewId);
            await PopulateRecruitersSelectList(feedback.RecruiterId);
            return View(feedback);
        }

        await feedbackService.CreateFeedbackAsync(feedback, currentRecruiterId, isInterviewer);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Interviewer")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        var (feedback, forbidden) = await feedbackService.GetFeedbackForEditAsync(id.Value, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

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
        if (id != feedback.Id)
        {
            return NotFound();
        }

        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        if (!ModelState.IsValid)
        {
            await PopulateInterviewsSelectList(feedback.InterviewId);
            await PopulateRecruitersSelectList(feedback.RecruiterId);
            return View(feedback);
        }

        var (success, error, forbidden) = await feedbackService.UpdateFeedbackAsync(feedback, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        if (!success)
        {
            return NotFound();
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

        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        var (feedback, forbidden) = await feedbackService.GetFeedbackForEditAsync(id.Value, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        if (feedback is null)
        {
            return NotFound();
        }

        var details = await feedbackService.GetFeedbackDetailsAsync(id.Value);
        return details is null ? NotFound() : View(details);
    }

    [Authorize(Roles = "Admin,Interviewer")]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        var (success, error, forbidden) = await feedbackService.DeleteFeedbackAsync(id, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateInterviewsSelectList(int? selectedInterviewId = null)
    {
        var items = await feedbackService.GetInterviewsDropdownAsync();
        ViewData["InterviewId"] = new SelectList(items.Select(i => new { i.Id, i.Label }), "Id", "Label", selectedInterviewId);
    }

    private async Task PopulateRecruitersSelectList(int? selectedRecruiterId = null)
    {
        var recruiters = await feedbackService.GetRecruitersDropdownAsync();
        ViewData["RecruiterId"] = new SelectList(recruiters, "Id", "FullName", selectedRecruiterId);
    }

    private async Task<int?> GetCurrentRecruiterId()
    {
        return await feedbackService.GetRecruiterIdForUserAsync(
            User.FindFirstValue(IdentityRecruiterSyncService.RecruiterIdClaim),
            User.FindFirstValue(ClaimTypes.NameIdentifier));
    }
}
