using System.Security.Claims;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Interviewer")]
public class SoftSkillAssessmentsController(ISoftSkillAssessmentService assessmentService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var assessments = await assessmentService.GetAssessmentsAsync();
        return View(assessments);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var assessment = await assessmentService.GetAssessmentDetailsAsync(id.Value);
        if (assessment is null)
        {
            return NotFound();
        }

        var isInterviewer = User.IsInRole("Interviewer") && !User.IsInRole("Admin") && !User.IsInRole("Recruiter");
        if (isInterviewer)
        {
            var currentRecruiterId = await GetCurrentRecruiterId();
            if (assessment.RecruiterId != currentRecruiterId)
            {
                return Forbid();
            }
        }

        return View(assessment);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateCandidatesSelectList();
        return View(new SoftSkillAssessment());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CandidateId,Communication,Teamwork,Responsibility,StressResistance,Leadership,OverallComment")] SoftSkillAssessment assessment)
    {
        var currentRecruiterId = await GetCurrentRecruiterId();
        var isInterviewer = User.IsInRole("Interviewer");

        if (!ModelState.IsValid)
        {
            await PopulateCandidatesSelectList(assessment.CandidateId);
            return View(assessment);
        }

        await assessmentService.CreateAssessmentAsync(assessment, currentRecruiterId, isInterviewer);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        var (assessment, forbidden) = await assessmentService.GetAssessmentForEditAsync(id.Value, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        if (assessment is null)
        {
            return NotFound();
        }

        await PopulateCandidatesSelectList(assessment.CandidateId);
        return View(assessment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CandidateId,RecruiterId,Communication,Teamwork,Responsibility,StressResistance,Leadership,OverallComment,CreatedAt")] SoftSkillAssessment assessment)
    {
        if (id != assessment.Id)
        {
            return NotFound();
        }

        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        if (!ModelState.IsValid)
        {
            await PopulateCandidatesSelectList(assessment.CandidateId);
            return View(assessment);
        }

        var (success, error, forbidden) = await assessmentService.UpdateAssessmentAsync(assessment, currentRecruiterId, isAdmin);
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

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        var (assessment, forbidden) = await assessmentService.GetAssessmentForEditAsync(id.Value, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        if (assessment is null)
        {
            return NotFound();
        }

        var details = await assessmentService.GetAssessmentDetailsAsync(id.Value);
        return details is null ? NotFound() : View(details);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var currentRecruiterId = await GetCurrentRecruiterId();
        var isAdmin = User.IsInRole("Admin");

        var (success, error, forbidden) = await assessmentService.DeleteAssessmentAsync(id, currentRecruiterId, isAdmin);
        if (forbidden)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateCandidatesSelectList(int? selectedCandidateId = null)
    {
        var candidates = await assessmentService.GetCandidatesDropdownAsync();
        ViewData["CandidateId"] = new SelectList(candidates, "Id", "FullName", selectedCandidateId);
    }

    private async Task<int?> GetCurrentRecruiterId()
    {
        return await assessmentService.GetRecruiterIdForUserAsync(
            User.FindFirstValue(IdentityRecruiterSyncService.RecruiterIdClaim),
            User.FindFirstValue(ClaimTypes.NameIdentifier));
    }
}
